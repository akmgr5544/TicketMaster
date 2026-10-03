using Events.Domain.Exceptions;

namespace Events.Domain.ValueObjects;

public sealed record TicketPrice
{
    public TicketPrice(decimal amount, string currency)
    {
        if (amount <= 0)
            throw new EventsDomainException($"A ticket price must be positive, but was {amount}");

        // ISO 4217 codes are three letters; PaymentSystem and the PSPs take the code as given.
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetter))
            throw new EventsDomainException($"'{currency}' is not a three-letter currency code");

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    // Rehydration only, like the entities: the stored values are not re-validated on load.
    private TicketPrice()
    {
        Currency = null!;
    }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
}
