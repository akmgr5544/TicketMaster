namespace PaymentSystem.Enums;

// The domain's own, so it never references the PSP library; ProcessRefund translates the PSP's answer into it.
public enum RefundOrderStatus
{
    // Recorded, not yet answered by the PSP.
    Pending,
    Succeeded,
    Failed
}
