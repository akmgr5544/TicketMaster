using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentSystem.Features.RefundOrders;

// Records the PSP's answer to a refund. A refund that went through reverses the money in the same transaction —
// the seller's wallet debited and a reversing ledger pair written beside the pay-in, never in its place.
public sealed class RecordRefundOutcomeHandler(ILogger<RecordRefundOutcomeHandler> logger)
{
    public async Task<OutgoingMessages> Handle(RecordRefundOutcome message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var refund = await context.RefundOrders
            .SingleOrDefaultAsync(r => r.RefundOrderId == message.RefundOrderId, cancellationToken);

        // Already recorded: a redelivery.
        if (refund is null || refund.Status != RefundOrderStatus.Pending)
            return outgoing;

        // A refused refund is not retried; a person has to return the money, and Bookings is not told it was refunded.
        if (!message.Accepted)
        {
            refund.MarkAsFailed();
            logger.LogError("Refund {RefundOrderId} was refused by the PSP: {Reason}; it needs a manual refund.",
                refund.RefundOrderId, message.FailureReason);
            return outgoing;
        }

        refund.MarkAsSettled(message.ProviderReference ?? string.Empty);

        var checkout = await context.PaymentEvents
            .SingleAsync(e => e.PaymentOrders.Any(o => o.PaymentOrderId == refund.PaymentOrderId), cancellationToken);
        var order = checkout.FindOrder(refund.PaymentOrderId);

        await ReverseMoneyAsync(checkout, order, refund, context, cancellationToken);

        var refundedBefore = await context.RefundOrders
            .Where(r => r.PaymentOrderId == order.PaymentOrderId
                        && r.RefundOrderId != refund.RefundOrderId
                        && r.Status == RefundOrderStatus.Succeeded)
            .ToListAsync(cancellationToken);

        // Refunds are whole, so one that went through gives back what was left on the order. Earlier refunds count
        // only if they went through, which a failed attempt before this one did not.
        if (refund.RefundAmount.Amount + refundedBefore.Sum(r => r.RefundAmount.Amount) < order.Amount.Amount)
            return outgoing;

        checkout.MarkOrderRefunded(order.PaymentOrderId);

        // Bookings releases the seats once it hears. A request id equal to the order id was made up here because
        // Bookings named none, so no refund id goes back.
        Guid? refundId = refund.RequestId == refund.PaymentOrderId ? null : refund.RequestId;
        outgoing.Add(new BookingRefundedIntegrationEvent(checkout.BookingId, refundId));

        return outgoing;
    }

    private static async Task ReverseMoneyAsync(PaymentEvent checkout, PaymentOrder order, RefundOrder refund,
        PaymentDbContext context, CancellationToken cancellationToken)
    {
        var currency = refund.RefundAmount.Currency;

        // Settlement credited this wallet when the order was paid, so it exists.
        var wallet = await context.Wallets
            .SingleAsync(w => w.UserId == order.SellerId && w.CurrentBalance.Currency == currency, cancellationToken);
        wallet.DebitFunds(refund.RefundAmount);

        // The money goes back the other way: the seller is debited and the buyer credited.
        context.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, order.SellerId.ToString(), EntryType.Debit, refund.RefundAmount));
        context.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, checkout.BuyerId.ToString(), EntryType.Credit, refund.RefundAmount));

        refund.ConfirmWalletReversal();
        refund.ConfirmLedgerReversal();
    }
}
