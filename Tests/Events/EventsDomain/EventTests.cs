using Events.Domain.DomainEvents;
using Events.Domain.Entities;
using Events.Domain.Enums;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;

namespace EventsDomain;

public class EventTests
{
    private static Venue AVenue() =>
        new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", new GeoLocation(40.1872, 44.5152), ["A1"]);

    private static Venue AnotherVenue() =>
        new("Tbilisi Arena", "26 May Square", new GeoLocation(41.7151, 44.8271), ["B1", "B2"]);

    private static Performer APerformer() => new("System of a Down", "Armenian-American rock band");

    private static DateTime FarEnoughOut => DateTime.UtcNow.AddDays(11);

    private static Event AnEvent() => new(FarEnoughOut, AVenue(), [APerformer()]);

    [Fact]
    public void Generates_its_own_id_on_creation()
    {
        var @event = new Event(FarEnoughOut, AVenue(), [APerformer()]);

        Assert.False(string.IsNullOrWhiteSpace(@event.Id));
    }

    [Fact]
    public void Rejects_a_start_date_inside_the_minimum_lead_time()
    {
        Assert.Throws<EventsDomainException>(() => new Event(DateTime.UtcNow.AddDays(9), AVenue(), [APerformer()]));
    }

    [Fact]
    public void Rejects_a_start_date_in_the_past()
    {
        Assert.Throws<EventsDomainException>(() => new Event(DateTime.UtcNow.AddDays(-1), AVenue(), [APerformer()]));
    }

    [Fact]
    public void Rejects_an_event_with_no_performers()
    {
        Assert.Throws<EventsDomainException>(() => new Event(FarEnoughOut, AVenue(), []));
    }

    [Fact]
    public void Embeds_the_venue_and_performers_it_was_created_with()
    {
        var venue = AVenue();
        var performer = APerformer();

        var @event = new Event(FarEnoughOut, venue, [performer]);

        Assert.Equal(venue.Id, @event.Venue.Id);
        Assert.Equal(performer.Id, Assert.Single(@event.Performers).Id);
    }

    [Fact]
    public void Reschedules_to_a_later_date()
    {
        var @event = new Event(FarEnoughOut, AVenue(), [APerformer()]);
        var newDate = DateTime.UtcNow.AddDays(30);

        @event.Reschedule(newDate);

        Assert.Equal(newDate, @event.StartDate);
    }

    [Fact]
    public void Refuses_to_reschedule_inside_the_minimum_lead_time()
    {
        var original = FarEnoughOut;
        var @event = new Event(original, AVenue(), [APerformer()]);

        Assert.Throws<EventsDomainException>(() => @event.Reschedule(DateTime.UtcNow.AddDays(9)));
        Assert.Equal(original, @event.StartDate);
    }

    [Fact]
    public void Does_not_share_performer_storage_with_the_caller()
    {
        var performers = new List<Performer> { APerformer() };
        var @event = new Event(FarEnoughOut, AVenue(), performers);

        performers.Add(APerformer());

        Assert.Single(@event.Performers);
    }

    // --- Creation state ---

    [Fact]
    public void Starts_out_scheduled_at_version_one()
    {
        var @event = AnEvent();

        Assert.Equal(EventStatus.Scheduled, @event.Status);
        Assert.Equal(1, @event.Version);
    }

    [Fact]
    public void Announces_its_creation()
    {
        var @event = AnEvent();

        var created = Assert.Single(@event.DomainEvents.OfType<EventCreatedDomainEvent>());
        Assert.Equal(@event.Id, created.EventId);
        Assert.Equal(1, created.Version);
    }

    // --- Version ---

    /// <summary>
    /// Every mutation has to move the version, because consumers use it to discard messages that
    /// arrive out of order. A mutation that forgets to bump it is silently unprotected.
    /// </summary>
    [Fact]
    public void Bumps_the_version_on_every_mutation()
    {
        var @event = AnEvent();

        @event.Reschedule(DateTime.UtcNow.AddDays(30));
        Assert.Equal(2, @event.Version);

        @event.Relocate(AnotherVenue());
        Assert.Equal(3, @event.Version);

        @event.ChangeLineup([APerformer()]);
        Assert.Equal(4, @event.Version);

        @event.Cancel();
        Assert.Equal(5, @event.Version);
    }

    // --- Reschedule ---

    [Fact]
    public void Announces_a_reschedule_with_the_new_date()
    {
        var @event = AnEvent();
        @event.ClearDomainEvents();
        var newDate = DateTime.UtcNow.AddDays(30);

        @event.Reschedule(newDate);

        var raised = Assert.Single(@event.DomainEvents.OfType<EventRescheduledDomainEvent>());
        Assert.Equal(@event.Id, raised.EventId);
        Assert.Equal(newDate, raised.StartDate);
        Assert.Equal(@event.Version, raised.Version);
    }

    [Fact]
    public void Does_not_announce_a_reschedule_it_refused()
    {
        var @event = AnEvent();
        @event.ClearDomainEvents();

        Assert.Throws<EventsDomainException>(() => @event.Reschedule(DateTime.UtcNow.AddDays(9)));

        Assert.Empty(@event.DomainEvents);
        Assert.Equal(1, @event.Version);
    }

    // --- Relocate ---

    [Fact]
    public void Relocates_to_a_different_venue()
    {
        var @event = AnEvent();
        var venue = AnotherVenue();

        @event.Relocate(venue);

        Assert.Equal(venue.Id, @event.Venue.Id);
        Assert.Equal(["B1", "B2"], @event.Venue.Seats);
    }

    [Fact]
    public void Announces_a_relocation_with_the_new_seats()
    {
        var @event = AnEvent();
        @event.ClearDomainEvents();
        var venue = AnotherVenue();

        @event.Relocate(venue);

        var raised = Assert.Single(@event.DomainEvents.OfType<EventRelocatedDomainEvent>());
        Assert.Equal(@event.Id, raised.EventId);
        Assert.Equal(venue.Id, raised.VenueId);
        Assert.Equal(["B1", "B2"], raised.Seats);
    }

    // --- Lineup ---

    [Fact]
    public void Changes_its_lineup()
    {
        var @event = AnEvent();
        var headliner = APerformer();
        var support = APerformer();

        @event.ChangeLineup([headliner, support]);

        Assert.Equal([headliner.Id, support.Id], @event.Performers.Select(p => p.Id));
    }

    [Fact]
    public void Refuses_a_lineup_with_no_performers()
    {
        var @event = AnEvent();
        var original = @event.Performers.Single().Id;

        Assert.Throws<EventsDomainException>(() => @event.ChangeLineup([]));

        Assert.Equal(original, @event.Performers.Single().Id);
    }

    // --- Cancel ---

    [Fact]
    public void Cancels_a_scheduled_event()
    {
        var @event = AnEvent();
        @event.ClearDomainEvents();

        @event.Cancel();

        Assert.Equal(EventStatus.Cancelled, @event.Status);
        var raised = Assert.Single(@event.DomainEvents.OfType<EventCancelledDomainEvent>());
        Assert.Equal(@event.Id, raised.EventId);
    }

    /// <summary>
    /// Cancelling twice is the same request arriving twice, not an error — so it is a no-op rather
    /// than a throw, and must not announce a second cancellation or move the version.
    /// </summary>
    [Fact]
    public void Treats_a_second_cancellation_as_a_no_op()
    {
        var @event = AnEvent();
        @event.Cancel();
        var versionAfterFirst = @event.Version;
        @event.ClearDomainEvents();

        @event.Cancel();

        Assert.Equal(EventStatus.Cancelled, @event.Status);
        Assert.Empty(@event.DomainEvents);
        Assert.Equal(versionAfterFirst, @event.Version);
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Refuses_to_change_a_cancelled_event(Action<Event> mutate)
    {
        var @event = AnEvent();
        @event.Cancel();
        @event.ClearDomainEvents();

        Assert.Throws<EventsDomainException>(() => mutate(@event));

        Assert.Empty(@event.DomainEvents);
    }

    public static TheoryData<Action<Event>> Mutations => new()
    {
        e => e.Reschedule(DateTime.UtcNow.AddDays(30)),
        e => e.Relocate(AnotherVenue()),
        e => e.ChangeLineup([APerformer()]),
    };

    // --- Domain event bookkeeping ---

    [Fact]
    public void Forgets_its_domain_events_once_they_are_cleared()
    {
        var @event = AnEvent();

        @event.ClearDomainEvents();

        Assert.Empty(@event.DomainEvents);
    }
}
