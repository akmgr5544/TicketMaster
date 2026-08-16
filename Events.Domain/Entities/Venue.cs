using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;

namespace Events.Domain.Entities;

public class Venue
{
    private readonly List<string> _seats;

    public Venue(string name, string address, GeoLocation location, IEnumerable<string> seats)
    {
        _seats = [..seats];

        if (_seats.Count == 0)
            throw new EventsDomainException("A venue must have at least one seat");

        Id = Guid.CreateVersion7().ToString();
        Name = NotBlank(name, nameof(name));
        Address = NotBlank(address, nameof(address));
        Location = location;
    }

    /// <summary>
    /// Rehydration only. Loading a stored venue is not creating one, so this deliberately skips the
    /// creation invariants — the serializer writes the persisted state straight onto the fields.
    /// </summary>
    private Venue()
    {
        _seats = [];
        Id = null!;
        Name = null!;
        Address = null!;
    }

    public string Id { get; private set; }
    public string Name { get; private set; }
    public string Address { get; private set; }
    public GeoLocation Location { get; private set; }
    public IReadOnlyList<string> Seats => _seats;

    public void Rename(string name) => Name = NotBlank(name, nameof(name));

    public void Relocate(GeoLocation location) => Location = location;

    private static string NotBlank(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new EventsDomainException($"Venue {field} must not be blank")
            : value;
}
