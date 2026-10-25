using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentSystem.Features.PaymentOrders;

// Applies a PSP's final answer about an order. A success is settled in the same transaction — the seller's wallet
// credited and a balanced ledger pair written — so an order is never paid but unsettled.
public sealed class RecordPaymentOutcomeHandler(ILogger<RecordPaymentOutcomeHandler> logger)
{
    public async Task<OutgoingMessages> Handle(RecordPaymentOutcome message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var checkout = await context.PaymentEvents
            .SingleOrDefaultAsync(e => e.PaymentOrders.Any(o => o.PaymentOrderId == message.PaymentOrderId), cancellationToken);

        if (checkout is null)
        {
            logger.LogWarning("A PSP reported on payment order {PaymentOrderId}, which does not exist.", message.PaymentOrderId);
            return outgoing;
        }

        if (!message.Succeeded)
        {
            if (checkout.FailOrder(message.PaymentOrderId) == OrderUpdate.Applied)
                outgoing.Add(new BookingPaymentFailedIntegrationEvent(checkout.BookingId));
            return outgoing;
        }

        switch (checkout.SucceedOrder(message.PaymentOrderId, message.ProviderReference))
        {
            case OrderUpdate.Applied:
                await SettleAsync(checkout, checkout.FindOrder(message.PaymentOrderId), context, cancellationToken);
                if (checkout.IsPaymentDone)
                    outgoing.Add(new BookingPaidIntegrationEvent(checkout.BookingId));
                break;

            // The PSP took money for an order that had already expired or been cancelled: nothing here can undo
            // that, so a person must refund it.
            case OrderUpdate.Superseded:
                logger.LogError("Payment order {PaymentOrderId} was paid at the PSP ({Reference}) after it failed here; it needs a manual refund.",
                    message.PaymentOrderId, message.ProviderReference);
                break;

            case OrderUpdate.NotStarted:
                logger.LogWarning("A PSP reported success for payment order {PaymentOrderId}, which was never started.", message.PaymentOrderId);
                break;
        }

        return outgoing;
    }

    private static async Task SettleAsync(PaymentEvent checkout, PaymentOrder order, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var currency = order.Amount.Currency;

        var wallet = await context.Wallets
            .SingleOrDefaultAsync(w => w.UserId == order.SellerId && w.CurrentBalance.Currency == currency, cancellationToken);

        if (wallet is null)
        {
            wallet = new Wallet(order.SellerId, currency);
            context.Wallets.Add(wallet);
        }

        wallet.CreditFunds(order.Amount);

        // Money leaves the buyer and reaches the seller: one debit and one credit for the same amount.
        context.LedgerEntries.Add(LedgerEntry.Create(order.PaymentOrderId, RefType.Payment, checkout.BuyerId.ToString(), EntryType.Debit, order.Amount));
        context.LedgerEntries.Add(LedgerEntry.Create(order.PaymentOrderId, RefType.Payment, order.SellerId.ToString(), EntryType.Credit, order.Amount));

        checkout.MarkOrderSettled(order.PaymentOrderId);
    }
}
