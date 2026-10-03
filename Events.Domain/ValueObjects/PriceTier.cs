using Events.Domain.Exceptions;

namespace Events.Domain.ValueObjects;

// A named group of the event's seats sold at their own price, in the event's currency. A seat in no tier sells at
// the event's base price.
public sealed class PriceTier
{
    public const int NameMaxLength = 50;

    private readonly List<string> _seats;

    public PriceTier(string name, decimal amount, IEnumerable<string> seats)
    {
        _seats = [..seats.Distinct()];

        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
            throw new EventsDomainException($"A price tier needs a name of at most {NameMaxLength} characters");

        if (amount <= 0)
            throw new EventsDomainException($"Price tier '{name}' must have a positive price, but was {amount}");

        if (_seats.Count == 0)
            throw new EventsDomainException($"Price tier '{name}' must cover at least one seat");

        Name = name.Trim();
        Amount = amount;
    }

    // Rehydration only, like the entities: the stored values are not re-validated on load.
    private PriceTier()
    {
        _seats = [];
        Name = null!;
    }

    public string Name { get; private set; }
    public decimal Amount { get; private set; }
    public IReadOnlyList<string> Seats => _seats;

    // What the tier still covers at a venue with these seats; null once it covers none of them.
    internal PriceTier? NarrowedTo(IReadOnlySet<string> venueSeats)
    {
        var kept = _seats.Where(venueSeats.Contains).ToList();
        return kept.Count == 0 ? null : new PriceTier(Name, Amount, kept);
    }
}
