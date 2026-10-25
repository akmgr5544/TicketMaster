using PaymentSystem.Domain.Exceptions;

namespace PaymentSystem.Domain.Shared;

public sealed record Money
{
    public const int Precision = 18;
    public const int Scale = 2;

    public decimal Amount { get; private init; }
    public string Currency { get; private init; } = null!;

    private Money() { }

    public static Money Create(decimal amount, string currency)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsLetter))
            throw new PaymentDomainException("Currency must be a 3-letter ISO 4217 code.");

        if (amount < 0)
            throw new PaymentDomainException("Amount cannot be negative.");

        // Anything finer than the column's scale would be rounded by Postgres, and the PSP would charge a different amount.
        if (decimal.Round(amount, Scale) != amount)
            throw new PaymentDomainException($"Amount cannot have more than {Scale} decimal places.");

        return new Money { Amount = amount, Currency = currency.ToUpperInvariant() };
    }

    public static Money Zero(string currency) => Create(0m, currency);

    public bool IsZero => Amount == 0m;

    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new PaymentDomainException($"Cannot add {right.Currency} to {left.Currency}.");

        return Create(left.Amount + right.Amount, left.Currency);
    }

    // Money is never negative, so subtracting more than there is throws.
    public static Money operator -(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new PaymentDomainException($"Cannot subtract {right.Currency} from {left.Currency}.");

        return Create(left.Amount - right.Amount, left.Currency);
    }
}
