using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Domain;

public class Wallet
{
    public Guid WalletId { get; private set; }
    public Guid UserId { get; private set; }
    // One wallet per user per currency: the balance carries the currency it is kept in.
    public Money CurrentBalance { get; private set; } = null!;
    public DateTime UpdatedAt { get; private set; }

    private Wallet() { }

    public Wallet(Guid userId, string currency)
    {
        WalletId = Guid.NewGuid();
        UserId = userId;
        CurrentBalance = Money.Zero(currency);
        UpdatedAt = DateTime.UtcNow;
    }

    public void CreditFunds(Money amount)
    {
        if (amount.IsZero) throw new PaymentDomainException("Credit increments must be greater than zero.");
        CurrentBalance += amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DebitFunds(Money amount)
    {
        if (amount.IsZero) throw new PaymentDomainException("Debit values must be greater than zero.");
        if (CurrentBalance.Amount < amount.Amount) throw new PaymentDomainException("Insufficient ledger funds to complete execution.");

        CurrentBalance -= amount;
        UpdatedAt = DateTime.UtcNow;
    }
}
