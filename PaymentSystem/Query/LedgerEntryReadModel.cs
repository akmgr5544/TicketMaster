namespace PaymentSystem.Query;

public class LedgerEntryReadModel
{
    public long EntryId { get; init; }
    public Guid TransactionRefId { get; init; }
    public string RefType { get; init; } = null!;
    public string AccountId { get; init; } = null!;
    public string EntryType { get; init; } = null!;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = null!;
    public DateTime CreatedAt { get; init; }
}
