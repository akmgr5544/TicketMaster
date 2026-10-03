using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class LedgerEntry
{
    public Guid PaymentOrderId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryType Type { get; private set; }
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
