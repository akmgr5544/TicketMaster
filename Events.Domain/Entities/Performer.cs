using Events.Domain.Exceptions;

namespace Events.Domain.Entities;

public class Performer
{
    public Performer(string name, string description)
    {
        Id = Guid.CreateVersion7().ToString();
        Name = NotBlank(name);
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

    public void Rename(string name) => Name = NotBlank(name);

    /// <summary>
    /// A description is optional, so unlike the name this accepts blank — clearing it is a
    /// legitimate edit, not a broken invariant.
    /// </summary>
    public void ChangeDescription(string description) => Description = description;

    private static string NotBlank(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new EventsDomainException("Performer name must not be blank")
            : value;
}
