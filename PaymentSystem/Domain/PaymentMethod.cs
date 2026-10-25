using PaymentSystem.Domain.Exceptions;

namespace PaymentSystem.Domain;

public class PaymentMethod
{
    public Guid Id { get; private set; }

    // References external aggregate by ID only
    public string UserId { get; private set; }

    public string PspName { get; private set; }
    public string PspToken { get; private set; } // PCI safe token
    public bool IsDefault { get; private set; }

    private PaymentMethod() { } // EF Core fallback

    private PaymentMethod(string userId, string pspName, string pspToken)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        PspName = pspName;
        PspToken = pspToken;
    }

    public static PaymentMethod Create(string userId, string pspName, string pspToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new PaymentDomainException("A payment method must belong to a user.");

        if (string.IsNullOrWhiteSpace(pspName))
            throw new PaymentDomainException("A payment method must name its payment provider.");

        if (string.IsNullOrWhiteSpace(pspToken))
            throw new PaymentDomainException("A payment method must carry the provider's token.");

        return new PaymentMethod(userId, pspName, pspToken);
    }

    // Internal business domain operation
    public void MarkAsDefault()
    {
        IsDefault = true;
    }

    public void ClearDefault()
    {
        IsDefault = false;
    }
}
