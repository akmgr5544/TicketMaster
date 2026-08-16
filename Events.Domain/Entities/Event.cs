using Events.Domain.Exceptions;

namespace Events.Domain.Entities;

public class Event
{
    /// <summary>
    /// How far ahead an event must be scheduled. Tickets are created downstream in response to
    /// this event being published, so there has to be time for that to happen before doors open.
    /// </summary>
    public static readonly TimeSpan MinimumLeadTime = TimeSpan.FromDays(10);

    private readonly List<Performer> _performers;

    public Event(DateTime startDate, Venue venue, IEnumerable<Performer> performers)
    {
        _performers = [..performers];

        if (_performers.Count == 0)
            throw new EventsDomainException("An event must have at least one performer");

        Id = Guid.CreateVersion7().ToString();
        StartDate = FarEnoughOut(startDate);
        Venue = venue;
    }

    /// <summary>
    /// Rehydration only. This is why the lead-time rule lives in the public constructor rather than
    /// a property setter — loading an event that has already happened must not re-run it.
    /// </summary>
    private Event()
    {
        _performers = [];
        Id = null!;
        Venue = null!;
    }

    public string Id { get; private set; }
    public DateTime StartDate { get; private set; }

    /// <summary>
    /// A snapshot of the venue as it was when the event was created. Renaming the venue in the
    /// venues container deliberately does not rewrite this copy.
    /// </summary>
    public Venue Venue { get; private set; }

    /// <summary>Snapshots of the performers, on the same terms as <see cref="Venue"/>.</summary>
    public IReadOnlyList<Performer> Performers => _performers;

    public void Reschedule(DateTime startDate) => StartDate = FarEnoughOut(startDate);

    private static DateTime FarEnoughOut(DateTime startDate) =>
        startDate < DateTime.UtcNow.Add(MinimumLeadTime)
            ? throw new EventsDomainException(
                $"An event must start at least {MinimumLeadTime.TotalDays} days from now")
            : startDate;
}
