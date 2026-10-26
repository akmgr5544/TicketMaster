using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentSystem.Features.RefundOrders;

// Records the PSP's answer to a refund, from ProcessRefund, a webhook or the refund reconciler. A refund that went
// through reverses the money in the same transaction — the seller's wallet debited and a reversing ledger pair written
// beside the pay-in, never in its place. One the PSP accepted but has not finished moves nothing until it does.
public sealed class RecordRefundOutcomeHandler(TimeProvider clock, ILogger<RecordRefundOutcomeHandler> logger)
{
    public async Task<OutgoingMessages> Handle(RecordRefundOutcome message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var refund = await context.RefundOrders
            .SingleOrDefaultAsync(r => r.RefundOrderId == message.RefundOrderId, cancellationToken);
        if (refund is null)
            return outgoing;

        if (refund.Status == RefundOrderStatus.Succeeded)
        {
            // An acceptance is a redelivery; a refusal means the PSP took the refund back after reporting it done.
            if (!message.Accepted)
            {
                var bookingId = await UndoRefundAsync(refund, message.FailureReason, context, cancellationToken);
                outgoing.Add(new BookingRefundFailedIntegrationEvent(bookingId, RefundIdFor(refund)));
            }
            return outgoing;
        }

        // Failed is final: an answer arriving after it is a redelivery or out of order.
        if (!refund.IsUnanswered)
            return outgoing;

        // A refused refund is not retried; a person has to return the money. Bookings is not told it was refunded, but
        // the buyer is told it failed rather than left waiting for money that is not coming.
        if (!message.Accepted)
        {
            refund.MarkAsFailed();
            logger.LogError("Refund {RefundOrderId} was refused by the PSP: {Reason}; it needs a manual refund.",
                refund.RefundOrderId, message.FailureReason);
            var (refusedCheckout, _) = await LoadOrderAsync(refund, context, cancellationToken);
            outgoing.Add(new BookingRefundFailedIntegrationEvent(refusedCheckout.BookingId, RefundIdFor(refund)));
            return outgoing;
        }

        if (!message.Final)
        {
            if (refund.Status == RefundOrderStatus.Pending)
                refund.MarkAsProcessing(message.ProviderReference ?? string.Empty, clock.GetUtcNow().UtcDateTime);
            return outgoing;
        }

        refund.MarkAsSettled(message.ProviderReference ?? refund.PspRefundReference ?? string.Empty,
            clock.GetUtcNow().UtcDateTime);

        var (checkout, order) = await LoadOrderAsync(refund, context, cancellationToken);

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

        // Bookings releases the seats once it hears.
        outgoing.Add(new BookingRefundedIntegrationEvent(checkout.BookingId, RefundIdFor(refund)));

        return outgoing;
    }

    // A request id equal to the order id was made up here because Bookings named none, so no refund id goes back.
    private static Guid? RefundIdFor(RefundOrder refund) =>
        refund.RequestId == refund.PaymentOrderId ? null : refund.RequestId;

    private static async Task<(PaymentEvent Checkout, PaymentOrder Order)> LoadOrderAsync(RefundOrder refund,
        PaymentDbContext context, CancellationToken cancellationToken)
    {
        var checkout = await context.PaymentEvents
            .SingleAsync(e => e.PaymentOrders.Any(o => o.PaymentOrderId == refund.PaymentOrderId), cancellationToken);
        return (checkout, checkout.FindOrder(refund.PaymentOrderId));
    }

    private static async Task ReverseMoneyAsync(PaymentEvent checkout, PaymentOrder order, RefundOrder refund,
        PaymentDbContext context, CancellationToken cancellationToken)
    {
        // Settlement credited this wallet when the order was paid, so it exists.
        var wallet = await SellerWalletAsync(order, refund, context, cancellationToken);
        wallet.DebitFunds(refund.RefundAmount);

        // The money goes back the other way: the seller is debited and the buyer credited.
        context.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, order.SellerId.ToString(), EntryType.Debit, refund.RefundAmount));
        context.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, checkout.BuyerId.ToString(), EntryType.Credit, refund.RefundAmount));

        refund.ConfirmWalletReversal();
        refund.ConfirmLedgerReversal();
    }

    // The buyer never got the money: it is the seller's again, and the order is paid. The ledger is append-only, so the
    // refund's pair stays and a pair the other way is written beside it. Bookings has already released the seats; the
    // buyer, told they were refunded, is now told it failed, and a person has to return the money. Returns the booking.
    private async Task<long> UndoRefundAsync(RefundOrder refund, string? reason, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var (checkout, order) = await LoadOrderAsync(refund, context, cancellationToken);

        var wallet = await SellerWalletAsync(order, refund, context, cancellationToken);
        wallet.CreditFunds(refund.RefundAmount);

        context.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, order.SellerId.ToString(), EntryType.Credit, refund.RefundAmount));
        context.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, checkout.BuyerId.ToString(), EntryType.Debit, refund.RefundAmount));

        refund.MarkAsFailedAfterSettlement();
        checkout.ReinstateOrderPayment(order.PaymentOrderId);

        logger.LogCritical(
            "Refund {RefundOrderId} of booking {BookingId} failed after the PSP reported it done: {Reason}. The buyer was " +
            "told they were refunded and has not been; a person must return the money.",
            refund.RefundOrderId, checkout.BookingId, reason);
        return checkout.BookingId;
    }

    private static Task<Wallet> SellerWalletAsync(PaymentOrder order, RefundOrder refund, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var currency = refund.RefundAmount.Currency;
        return context.Wallets
            .SingleAsync(w => w.UserId == order.SellerId && w.CurrentBalance.Currency == currency, cancellationToken);
    }
}
