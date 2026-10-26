using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

// A difference between our books and what a PSP settled, for a person to resolve. Unique per provider, kind and
// reference — the PSP's reference for what it settled, or ours for what it never did — because that names one
// finding: a later run finding it again adds nothing. Amounts are plain columns rather than Money: on a currency
// mismatch the two sides are in different currencies, and either side may be missing.
public class SettlementDiscrepancy
{
    public Guid SettlementDiscrepancyId { get; private set; }
    public string Provider { get; private set; } = null!;
    public SettlementDiscrepancyKind Kind { get; private set; }
    public string Reference { get; private set; } = null!;
    public Guid? PaymentOrderId { get; private set; }
    public Guid? RefundOrderId { get; private set; }
    public decimal? ExpectedAmount { get; private set; }
    public string? ExpectedCurrency { get; private set; }
    public decimal? ActualAmount { get; private set; }
    public string? ActualCurrency { get; private set; }
    public string Detail { get; private set; } = null!;
    public DateTime FoundAt { get; private set; }

    private SettlementDiscrepancy() { }

    public SettlementDiscrepancy(string provider, SettlementDiscrepancyKind kind, string reference, string detail,
        DateTime foundAt, Guid? paymentOrderId = null, Guid? refundOrderId = null,
        decimal? expectedAmount = null, string? expectedCurrency = null,
        decimal? actualAmount = null, string? actualCurrency = null)
    {
        SettlementDiscrepancyId = Guid.CreateVersion7();
        Provider = provider;
        Kind = kind;
        Reference = reference;
        Detail = detail;
        FoundAt = foundAt;
        PaymentOrderId = paymentOrderId;
        RefundOrderId = refundOrderId;
        ExpectedAmount = expectedAmount;
        ExpectedCurrency = expectedCurrency;
        ActualAmount = actualAmount;
        ActualCurrency = actualCurrency;
    }
}
