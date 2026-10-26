namespace PaymentSystem.Enums;

// The domain's own, so it never references the PSP library; the settlement reconciler translates the PSP's lines.
public enum SettlementEntryKind
{
    Charge,
    Refund,
    RefundFailure
}
