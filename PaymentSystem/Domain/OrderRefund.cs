using PaymentSystem.Domain.Exceptions;

namespace PaymentSystem.Domain;

// One part of an order given back. RefundId is the caller's name for the part, so a redelivered request finds the
// part it already made instead of making another.
public class OrderRefund
{
    public const int ProviderReferenceMaxLength = 200;

    public Guid PaymentOrderId { get; private set; }
    public Guid RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string ProviderReference { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    private OrderRefund()
    {
    }

    internal static OrderRefund Create(Guid paymentOrderId, Guid refundId, decimal amount, string providerReference)
    {
        if (refundId == Guid.Empty)
            throw new PaymentDomainException("A refund needs an id.");
        if (string.IsNullOrWhiteSpace(providerReference))
            throw new PaymentDomainException("A refund needs the provider's reference for it.");
        if (providerReference.Length > ProviderReferenceMaxLength)
            throw new PaymentDomainException($"A refund reference cannot be longer than {ProviderReferenceMaxLength} characters.");

        return new OrderRefund
        {
            PaymentOrderId = paymentOrderId,
            RefundId = refundId,
            Amount = amount,
            ProviderReference = providerReference
        };
    }
}
