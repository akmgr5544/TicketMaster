using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

// A line a PSP settled, kept so a later run can tell what the PSP has and has not settled. Unique per provider,
// reference and kind: the same day is read on several runs, and a refund and its failure share a reference.
public class SettlementRecord
{
    public Guid SettlementRecordId { get; private set; }
    public string Provider { get; private set; } = null!;
    public string ProviderReference { get; private set; } = null!;
    public SettlementEntryKind Kind { get; private set; }
    public Money Amount { get; private set; } = null!;
    public Guid? PaymentOrderId { get; private set; }
    public Guid? RefundId { get; private set; }
    public DateTime FirstSeenAt { get; private set; }

    private SettlementRecord() { }

    public SettlementRecord(string provider, string providerReference, SettlementEntryKind kind, Money amount,
        Guid? paymentOrderId, Guid? refundId, DateTime seenAt)
    {
        SettlementRecordId = Guid.CreateVersion7();
        Provider = provider;
        ProviderReference = providerReference;
        Kind = kind;
        Amount = amount;
        PaymentOrderId = paymentOrderId;
        RefundId = refundId;
        FirstSeenAt = seenAt;
    }
}
