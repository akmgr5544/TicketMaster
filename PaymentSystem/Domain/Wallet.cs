using PaymentSystem.Domain.Abstractions;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Domain;

// One wallet per seller per currency: a balance is only meaningful in a single currency.
public class Wallet : Entity
{
    public Guid WalletId { get; private set; }
    public Guid OwnerId { get; private set; }
    public decimal Balance { get; private set; }
    public string Currency { get; private set; } = null!;
    // Stamped by AuditTimestampsInterceptor on save.
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Wallet()
    {
    }

    public static Wallet Create(Guid ownerId, string currency)
    {
        if (ownerId == Guid.Empty)
            throw new PaymentDomainException("A wallet needs an owner.");
        CurrencyCode.EnsureValid(currency);

        return new Wallet
        {
            WalletId = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Currency = currency,
            Balance = 0m
        };
    }

    // Not idempotent on its own: PaymentOrder.WalletUpdated, saved in the same transaction, is what stops a
    // redelivered settlement from crediting twice.
    public void Credit(decimal amount, string currency)
    {
        MoneyAmount.EnsurePositiveAndStorable(amount, "A credit");
        if (currency != Currency)
            throw new PaymentDomainException($"Cannot credit {currency} to a {Currency} wallet.");
        // Subtracted rather than added so the check itself cannot overflow; both sides are already storable.
        if (amount > MoneyAmount.MaxStorable - Balance)
            throw new PaymentDomainException("The credit would take the balance past the largest storable amount.");

        Balance += amount;
    }

    // Allowed below zero on purpose: the provider has already returned the money, and refusing to record it would
    // leave the wallet claiming funds the seller no longer has. A negative balance is what the seller owes.
    public void Debit(decimal amount, string currency)
    {
        MoneyAmount.EnsurePositiveAndStorable(amount, "A debit");
        if (currency != Currency)
            throw new PaymentDomainException($"Cannot debit {currency} from a {Currency} wallet.");
        if (amount > MoneyAmount.MaxStorable + Balance)
            throw new PaymentDomainException("The debit would take the balance past the smallest storable amount.");

        Balance -= amount;
    }
}
