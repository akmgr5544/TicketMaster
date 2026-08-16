using Events.Domain.Exceptions;

namespace Events.Domain.Entities;

public class Performer
{
    public Performer(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new EventsDomainException("Performer name must not be blank");

        Id = Guid.CreateVersion7().ToString();
        Name = name;
        Description = description;
    }

    /// <summary>
    /// Rehydration only — see the note on <see cref="Venue"/>.
    /// </summary>
    private Performer()
    {
        Id = null!;
        Name = null!;
        Description = null!;
    }

    public string Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
}
