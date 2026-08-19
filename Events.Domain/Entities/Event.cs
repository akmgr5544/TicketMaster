using Events.Domain.Abstractions;
using Events.Domain.DomainEvents;
using Events.Domain.Enums;
using Events.Domain.Exceptions;

namespace Events.Domain.Entities;

public class Event : Entity
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
        Status = EventStatus.Scheduled;
        Version = 1;

        Raise(new EventCreatedDomainEvent(Id, Version, venue.Id, StartDate, [..venue.Seats]));
    }

    /// <summary>
    /// Rehydration only. This is why the lead-time rule lives in the public constructor rather than
    /// a property setter — loading an event that has already happened must not re-run it. It is
    /// also why nothing here raises a domain event: loading is not changing.
    /// </summary>
    private Event()
    {
        _performers = [];
        Id = null!;
        Venue = null!;
    }

    public string Id { get; private set; }
    public DateTime StartDate { get; private set; }
    public EventStatus Status { get; private set; }

    /// <summary>
    /// Incremented by every mutation and carried on every domain event. Consumers in other services
    /// compare it against what they have already applied so that a redelivered older message cannot
    /// overwrite a newer change. A mutation that forgets to bump it is silently unprotected.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// A snapshot of the venue as it was when the event was created or last relocated. Renaming the
    /// venue in the venues container deliberately does not rewrite this copy.
    /// </summary>
    public Venue Venue { get; private set; }

    /// <summary>Snapshots of the performers, on the same terms as <see cref="Venue"/>.</summary>
    public IReadOnlyList<Performer> Performers => _performers;

    public void Reschedule(DateTime startDate)
    {
        MustBeScheduled();

        StartDate = FarEnoughOut(startDate);

        Raise(new EventRescheduledDomainEvent(Id, Bump(), StartDate));
    }

    /// <summary>
    /// Moves the event to a different venue, replacing the embedded snapshot. The new venue's seats
    /// are almost certainly a different set, which is why the domain event carries them in full —
    /// downstream has to reconcile whatever it already holds against them.
    /// </summary>
    public void Relocate(Venue venue)
    {
        MustBeScheduled();

        Venue = venue;

        Raise(new EventRelocatedDomainEvent(Id, Bump(), venue.Id, StartDate, [..venue.Seats]));
    }

    public void ChangeLineup(IEnumerable<Performer> performers)
    {
        MustBeScheduled();

        // Materialised and validated before anything is replaced, so a rejected lineup leaves the
        // existing one intact.
        var replacement = performers.ToList();

        if (replacement.Count == 0)
            throw new EventsDomainException("An event must have at least one performer");

        _performers.Clear();
        _performers.AddRange(replacement);

        Raise(new EventLineupChangedDomainEvent(Id, Bump(), [.._performers.Select(p => p.Id)]));
    }

    /// <summary>
    /// Calls the event off. Idempotent on purpose: cancelling an already-cancelled event is the
    /// same request arriving twice, not an error, so it changes nothing and announces nothing.
    /// </summary>
    public void Cancel()
    {
        if (Status == EventStatus.Cancelled)
            return;

        Status = EventStatus.Cancelled;

        Raise(new EventCancelledDomainEvent(Id, Bump()));
    }

    private void MustBeScheduled()
    {
        if (Status == EventStatus.Cancelled)
            throw new EventsDomainException($"Event '{Id}' is cancelled and cannot be changed");
    }

    private long Bump() => ++Version;

    private static DateTime FarEnoughOut(DateTime startDate) =>
        startDate < DateTime.UtcNow.Add(MinimumLeadTime)
            ? throw new EventsDomainException(
                $"An event must start at least {MinimumLeadTime.TotalDays} days from now")
            : startDate;
}
