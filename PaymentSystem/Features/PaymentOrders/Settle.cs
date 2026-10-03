using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

            Wallet? created = null;
            if (!order.WalletUpdated)
            {
                var wallet = await FindWalletAsync(order, cancellationToken);
                if (wallet is null)
                {
                    wallet = created = Wallet.Create(order.MerchantId, order.Currency);
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

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            // Another settlement for this seller created the wallet between the lookup and this insert. Postgres
            // held the insert until that one committed, so its wallet is there to credit instead — and EF saved
            // inside a savepoint, so the transaction is still usable for the retry.
            catch (DbUpdateException exception) when (created is not null && IsWalletIndexViolation(exception))
            {
                context.Entry(created).State = EntityState.Detached;
                var winners = await FindWalletAsync(order, cancellationToken)
                              ?? throw new InvalidOperationException(
                                  $"The wallet for {order.MerchantId} in {order.Currency} was taken but cannot be found.");
                winners.Credit(order.Amount, order.Currency);
                await context.SaveChangesAsync(cancellationToken);
            }

            // Gated on every order being settled, not only on IsPaymentDone: when several orders succeed in
            // one save each gets its own event, and all of them would see the checkout done. Only the
            // settlement of the last one finds nothing left unsettled, so BookingPaid goes out once.
            if (checkout.IsPaymentDone && checkout.PaymentOrders.All(o => o is { WalletUpdated: true, LedgerUpdated: true }))
                await publisher.PublishAsync(new BookingPaidIntegrationEvent(checkout.BookingId), cancellationToken);
        }

        private Task<Wallet?> FindWalletAsync(PaymentOrder order, CancellationToken cancellationToken) =>
            context.Wallets.SingleOrDefaultAsync(w => w.OwnerId == order.MerchantId && w.Currency == order.Currency,
                cancellationToken);

        // Read from the model rather than spelled out, so renaming the index cannot silently turn the race back
        // into a 500. Any other unique violation — a second ledger pair above all — is a real fault and is rethrown.
        private bool IsWalletIndexViolation(DbUpdateException exception) =>
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
            && postgres.ConstraintName == context.Model.FindEntityType(typeof(Wallet))!.GetIndexes()
                .Single(index => index.IsUnique).GetDatabaseName();
    }
}
