using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class RefundOrder
{
    public Guid RefundOrderId { get; private set; }
    public Guid PaymentOrderId { get; private set; }
    // The refund id Bookings sent; unique per order, so a redelivered request is not refunded twice.
    public Guid RequestId { get; private set; }
    public Money RefundAmount { get; private set; } = null!;
    public RefundOrderStatus Status { get; private set; }
    public string RefundReason { get; private set; }
    public string? PspRefundReference { get; private set; }
    public bool WalletReversed { get; private set; }
    public bool LedgerReversed { get; private set; }
    // When the PSP accepted it without finishing; the refund reconciler asks again once it has waited a while.
    public DateTime? ProcessingSince { get; private set; }
    // When the PSP reported it done; settlement reconciliation expects the PSP to have settled it within a grace period.
    public DateTime? SucceededAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private RefundOrder()
    {
    }

    private RefundOrder(Guid paymentOrderId, Guid requestId, Money refundAmount, string reason)
    {
        RefundOrderId = Guid.NewGuid();
        PaymentOrderId = paymentOrderId;
        RequestId = requestId;
        RefundAmount = refundAmount;
        RefundReason = reason;
        Status = RefundOrderStatus.Pending;
        WalletReversed = false;
        LedgerReversed = false;
        CreatedAt = DateTime.UtcNow;
    }

    public static RefundOrder Create(Guid paymentOrderId, Guid requestId, Money refundAmount, string reason)
    {
        if (refundAmount.IsZero)
            throw new PaymentDomainException("Refund values must be positive numbers.");

        return new RefundOrder(paymentOrderId, requestId, refundAmount, reason);
    }

    public bool IsUnanswered => Status is RefundOrderStatus.Pending or RefundOrderStatus.Processing;

    public void MarkAsProcessing(string pspRefundReference, DateTime now)
    {
        if (Status != RefundOrderStatus.Pending)
            throw new PaymentDomainException($"Refund {RefundOrderId} is {Status}; only a pending refund can start processing.");

        Status = RefundOrderStatus.Processing;
        PspRefundReference = pspRefundReference;
        ProcessingSince = now;
    }

    public void MarkAsSettled(string pspRefundReference, DateTime? now = null)
    {
        if (!IsUnanswered)
            throw new PaymentDomainException($"Refund {RefundOrderId} is {Status} and cannot succeed now.");

        Status = RefundOrderStatus.Succeeded;
        PspRefundReference = pspRefundReference;
        SucceededAt = now ?? DateTime.UtcNow;
    }

    public void MarkAsFailed()
    {
        if (!IsUnanswered)
            throw new PaymentDomainException($"Refund {RefundOrderId} is {Status} and cannot fail now.");

        Status = RefundOrderStatus.Failed;
    }

    // The PSP took back a refund it had reported done. The caller has put the money back on the seller's side, so the
    // refund ends as if it had never moved anything: no longer reversed in the wallet or, net, in the ledger.
    public void MarkAsFailedAfterSettlement()
    {
        if (Status != RefundOrderStatus.Succeeded)
            throw new PaymentDomainException($"Refund {RefundOrderId} is {Status}; only a succeeded refund can fail after settling.");

        Status = RefundOrderStatus.Failed;
        WalletReversed = false;
        LedgerReversed = false;
    }
    public void ConfirmWalletReversal() => WalletReversed = true;
    public void ConfirmLedgerReversal() => LedgerReversed = true;
}