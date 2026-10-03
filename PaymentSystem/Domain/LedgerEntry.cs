using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class LedgerEntry
{
    public Guid PaymentOrderId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryType Type { get; private set; }
    public EntryReason Reason { get; private set; }
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

        return Pair(order, EntryReason.PayIn, debited: order.BuyerId, credited: order.MerchantId);
    }

    // The pay-in reversed: money leaves the seller and returns to the buyer. Written beside the pay-in pair, never
    // in place of it, so the ledger keeps the history and still balances to zero.
    public static IReadOnlyList<LedgerEntry> RecordRefund(PaymentOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Status != PaymentOrderStatus.Refunded)
            throw new PaymentDomainException("Only a refunded payment order has its refund written to the ledger.");

        return Pair(order, EntryReason.Refund, debited: order.MerchantId, credited: order.BuyerId);
    }

    private static IReadOnlyList<LedgerEntry> Pair(PaymentOrder order, EntryReason reason, Guid debited, Guid credited) =>
    [
        new LedgerEntry
        {
            PaymentOrderId = order.PaymentOrderId,
            AccountId = debited,
            Type = EntryType.Debit,
            Reason = reason,
            Amount = order.Amount,
            Currency = order.Currency
        },
        new LedgerEntry
        {
            PaymentOrderId = order.PaymentOrderId,
            AccountId = credited,
            Type = EntryType.Credit,
            Reason = reason,
            Amount = order.Amount,
            Currency = order.Currency
        }
    ];
}
