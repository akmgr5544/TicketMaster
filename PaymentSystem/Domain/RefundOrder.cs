using PaymentProvider.Models;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Domain;

public class RefundOrder
{
    public Guid RefundOrderId { get; private set; }
    public Guid PaymentOrderId { get; private set; }
    // The refund id Bookings sent; unique per order, so a redelivered request is not refunded twice.
    public Guid RequestId { get; private set; }
    public Money RefundAmount { get; private set; } = null!;
    public RefundStatus Status { get; private set; }
    public string RefundReason { get; private set; }
    public string? PspRefundReference { get; private set; }
    public bool WalletReversed { get; private set; }
    public bool LedgerReversed { get; private set; }
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
        Status = RefundStatus.Pending;
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

    public void MarkAsSettled(string pspRefundReference)
    {
        Status = RefundStatus.Succeeded;
        PspRefundReference = pspRefundReference;
    }

    public void MarkAsFailed() => Status = RefundStatus.Failed;
    public void ConfirmWalletReversal() => WalletReversed = true;
    public void ConfirmLedgerReversal() => LedgerReversed = true;
}