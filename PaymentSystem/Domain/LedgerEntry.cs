using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

// Append-only. Entries only come into being as a balanced debit/credit pair, so the ledger sums to zero per
// currency by construction rather than by a check someone has to remember to run.
public class LedgerEntry
{
    public Guid PaymentOrderId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    // Stamped by AuditTimestampsInterceptor on save.
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

        return
        [
            new LedgerEntry
            {
                PaymentOrderId = order.PaymentOrderId,
                AccountId = order.BuyerId,
                Type = EntryType.Debit,
                Amount = order.Amount,
                Currency = order.Currency
            },
            new LedgerEntry
            {
                PaymentOrderId = order.PaymentOrderId,
                AccountId = order.MerchantId,
                Type = EntryType.Credit,
                Amount = order.Amount,
                Currency = order.Currency
            }
        ];
    }
}
