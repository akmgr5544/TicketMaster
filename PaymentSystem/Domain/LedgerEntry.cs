using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class LedgerEntry
{
    public long EntryId { get; private set; } // Sequentially logged primary sequence key
    public Guid TransactionRefId { get; private set; } // References PaymentOrderId or RefundOrderId
    public RefType RefType { get; private set; }
    public string AccountId { get; private set; } // Target destination tracking balance segment
    public EntryType EntryType { get; private set; }
    public Money Amount { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    private LedgerEntry() { }

    private LedgerEntry(Guid transactionRefId, RefType refType, string accountId, EntryType entryType, Money amount)
    {
        TransactionRefId = transactionRefId;
        RefType = refType;
        AccountId = accountId;
        EntryType = entryType;
        Amount = amount;
        CreatedAt = DateTime.UtcNow;
    }

    public static LedgerEntry Create(Guid transactionRefId, RefType refType, string accountId, EntryType entryType,
        Money amount)
    {
        if (amount.IsZero) throw new PaymentDomainException("Ledger transaction volume details must be positive entries.");

        return new LedgerEntry(transactionRefId, refType, accountId, entryType, amount);
    }
}
