using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class PaymentOrder
{
    public Guid PaymentOrderId { get; private set; } // Client-Generated Idempotency validation key
    public Guid CheckoutId { get; private set; }
    public Guid SellerId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public PaymentOrderStatus Status { get; private set; }
    public string? PspTokenRef { get; private set; } // Outward transaction receipt tracing string from PSP
    // The PSP that took the order, so a refund goes back to the same one.
    public string? Provider { get; private set; }
    public bool WalletUpdated { get; private set; }
    public bool LedgerUpdated { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private PaymentOrder() { }

    internal PaymentOrder(Guid checkoutId, Guid sellerId, Money amount)
    {
        if (amount.IsZero) throw new PaymentDomainException("Order value must be positive.");

        PaymentOrderId = Guid.NewGuid();
        CheckoutId = checkoutId;
        SellerId = sellerId;
        Amount = amount;
        Status = PaymentOrderStatus.Init;
        WalletUpdated = false;
        LedgerUpdated = false;
        CreatedAt = DateTime.UtcNow;
    }

    internal void StartExecuting(string provider, string? pspTokenRef)
    {
        Status = PaymentOrderStatus.Executing;
        Provider = provider;
        PspTokenRef = pspTokenRef;
    }

    public void TransitionToSuccess(string pspTokenRef)
    {
        Status = PaymentOrderStatus.Success;
        PspTokenRef = pspTokenRef;
    }

    public void TransitionToFailed() => Status = PaymentOrderStatus.Failed;
    public void TransitionToRefunded() => Status = PaymentOrderStatus.Refunded;
    public void CompleteWalletDistribution() => WalletUpdated = true;
    public void CompleteLedgerBookkeeping() => LedgerUpdated = true;
}