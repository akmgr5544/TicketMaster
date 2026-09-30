using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Events;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.PaymentOrders;

// Runs inside the SaveChangesAsync that recorded the success, so under TransactionBehavior (or any caller
// transaction) the credit, the ledger pair and BookingPaid commit or roll back with that success.
public static class Settle
{
    internal sealed class Handler(PaymentDbContext context, IIntegrationEventPublisher publisher)
        : INotificationHandler<PaymentOrderSucceededDomainEvent>
    {
        public async Task Handle(PaymentOrderSucceededDomainEvent notification, CancellationToken cancellationToken)
        {
            // Identity resolution returns the instance already tracked by the save that raised the event.
            var checkout = await context.PaymentEvents
                .SingleAsync(e => e.CheckoutId == notification.CheckoutId, cancellationToken);
            var order = checkout.PaymentOrders.Single(o => o.PaymentOrderId == notification.PaymentOrderId);

            // The flags, saved with the money, are what make a repeated settlement a no-op.
            if (order is { WalletUpdated: true, LedgerUpdated: true })
                return;

            // "Every order succeeded" must be decided over committed state. The version check only guards the
            // checkout row: a caller that retried a conflict by reloading just the root passes it while still
            // tracking a sibling another writer has since settled, and the checkout would never be marked done.
            // Only unchanged siblings are refreshed, so nothing this save is writing is thrown away.
            foreach (var sibling in checkout.PaymentOrders.Where(o => o.PaymentOrderId != order.PaymentOrderId))
            {
                var entry = context.Entry(sibling);
                if (entry.State == EntityState.Unchanged)
                    await entry.ReloadAsync(cancellationToken);
            }

            if (!order.WalletUpdated)
            {
                var wallet = await context.Wallets
                    .SingleOrDefaultAsync(w => w.OwnerId == order.MerchantId && w.Currency == order.Currency,
                        cancellationToken);
                if (wallet is null)
                {
                    wallet = Wallet.Create(order.MerchantId, order.Currency);
                    context.Wallets.Add(wallet);
                }

                wallet.Credit(order.Amount, order.Currency);
                checkout.MarkWalletUpdated(order.PaymentOrderId);
            }

            if (!order.LedgerUpdated)
            {
                context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(order));
                checkout.MarkLedgerUpdated(order.PaymentOrderId);
            }

            await context.SaveChangesAsync(cancellationToken);

            // Gated on every order being settled, not only on IsPaymentDone: when several orders succeed in
            // one save each gets its own event, and all of them would see the checkout done. Only the
            // settlement of the last one finds nothing left unsettled, so BookingPaid goes out once.
            if (checkout.IsPaymentDone && checkout.PaymentOrders.All(o => o is { WalletUpdated: true, LedgerUpdated: true }))
                await publisher.PublishAsync(new BookingPaidIntegrationEvent(checkout.BookingId), cancellationToken);
        }
    }
}
