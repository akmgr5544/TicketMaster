using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class LedgerEntry
{
    public Guid PaymentOrderId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryType Type { get; private set; }
    public EntryReason Reason { get; private set; }
    // Which refund a refund pair is for, since an order refunded in parts has one pair per part. Null on a pay-in.
    public Guid? RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    private LedgerEntry()
    {
    }

    // Money leaves the buyer and reaches the seller.
    public static IReadOnlyList<LedgerEntry> RecordPayIn(PaymentOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Status != PaymentOrderStatus.Success)
            throw new PaymentDomainException("Only a successful payment order is written to the ledger.");

        return Pair(order, EntryReason.PayIn, order.Amount, refundId: null, debited: order.BuyerId, credited: order.MerchantId);
    }

    // The pay-in reversed, for as much as this refund gave back: money leaves the seller and returns to the buyer.
    // Written beside the pay-in pair, never in place of it, so the ledger keeps the history and still balances.
    public static IReadOnlyList<LedgerEntry> RecordRefund(PaymentOrder order, Guid refundId)
    {
        ArgumentNullException.ThrowIfNull(order);
        var refund = order.RefundById(refundId);

        return Pair(order, EntryReason.Refund, refund.Amount, refundId, debited: order.MerchantId, credited: order.BuyerId);
    }

    private static IReadOnlyList<LedgerEntry> Pair(PaymentOrder order, EntryReason reason, decimal amount, Guid? refundId,
        Guid debited, Guid credited) =>
    [
        new LedgerEntry
        {
            PaymentOrderId = order.PaymentOrderId,
            AccountId = debited,
            Type = EntryType.Debit,
            Reason = reason,
            RefundId = refundId,
            Amount = amount,
            Currency = order.Currency
        },
        new LedgerEntry
        {
            PaymentOrderId = order.PaymentOrderId,
            AccountId = credited,
            Type = EntryType.Credit,
            Reason = reason,
            RefundId = refundId,
            Amount = amount,
            Currency = order.Currency
        }
    ];
}
