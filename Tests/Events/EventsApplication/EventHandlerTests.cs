using Events.Application.CommandHandlers;
using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.Queries;
using Events.Application.QueryHandlers;
using Events.Domain.Entities;
using Events.Domain.Enums;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;
using EventsApplication.Fakes;
using TicketMaster.Common.IntegrationEvents;

namespace EventsApplication;

public class EventHandlerTests
{
    private readonly FakeEventRepository _events = new();
    private readonly FakeVenueRepository _venues = new();
    private readonly FakePerformerRepository _performers = new();
    private readonly FakeIntegrationEventPublisher _publisher = new();

    private static Venue AVenue(params string[] seats) =>
        new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", new GeoLocation(40.1872, 44.5152),
            seats.Length == 0 ? ["A1"] : seats);

    private static Performer APerformer() => new("System of a Down", "Armenian-American rock band");

    private static DateTime FarEnoughOut => DateTime.UtcNow.AddDays(11);

    private Event AnEvent()
    {
        var @event = new Event(FarEnoughOut, AVenue(), [APerformer()]);
        @event.ClearDomainEvents();
        _events.Seed(@event);
        return @event;
    }

    // --- Get ---

    [Fact]
    public async Task Get_returns_the_event_as_a_dto()
    {
        var @event = AnEvent();

        var result = await new GetEventQueryHandler(_events)
            .Handle(new GetEventQuery(@event.Id), CancellationToken.None);

        Assert.Equal(@event.Id, result.Id);
        Assert.Equal(@event.StartDate, result.StartDate);
        Assert.Equal(nameof(EventStatus.Scheduled), result.Status);
        Assert.Equal(@event.Version, result.Version);
        Assert.Equal(@event.Venue.Id, result.Venue.Id);
        Assert.Equal(@event.Performers.Single().Id, result.Performers.Single().Id);
    }

    [Fact]
    public async Task Get_throws_when_the_event_does_not_exist()
    {
        var handler = new GetEventQueryHandler(_events);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetEventQuery("missing"), CancellationToken.None));
    }

    // --- List ---

    [Fact]
    public async Task List_passes_the_continuation_token_through_in_both_directions()
    {
        AnEvent();
        AnEvent();
        _events.NextContinuationToken = "next-page";

        var result = await new ListEventsQueryHandler(_events)
            .Handle(new ListEventsQuery(10, "from-client"), CancellationToken.None);

        Assert.Equal("from-client", _events.LastContinuationTokenRequested);
        Assert.Equal("next-page", result.ContinuationToken);
        Assert.Equal(2, result.Items.Count);
    }

    // --- Create ---

    [Fact]
    public async Task Create_returns_the_id_and_announces_the_new_event()
    {
        var venue = AVenue("A1", "A2");
        _venues.Seed(venue);
        var performer = APerformer();
        _performers.Seed(performer);

        var id = await new CreateEventCommandHandler(_events, _venues, _performers, _publisher).Handle(
            new CreateEventCommand(FarEnoughOut, venue.Id, [performer.Id]),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(id));

        var published = _publisher.PublishedSingle<EventCreatedIntegrationEvent>();
        Assert.Equal(id, published.EventId);
        Assert.Equal(venue.Id, published.VenueId);
        Assert.Equal(["A1", "A2"], published.Seats);
        Assert.Equal(1, published.Version);
    }

    // --- Reschedule ---

    [Fact]
    public async Task Reschedule_writes_the_event_and_announces_the_new_date()
    {
        var @event = AnEvent();
        var newDate = DateTime.UtcNow.AddDays(40);

        await new RescheduleEventCommandHandler(_events, _publisher)
            .Handle(new RescheduleEventCommand(@event.Id, newDate), CancellationToken.None);

        Assert.Same(@event, Assert.Single(_events.Updated));

        var published = _publisher.PublishedSingle<EventRescheduledIntegrationEvent>();
        Assert.Equal(@event.Id, published.EventId);
        Assert.Equal(newDate, published.StartDate);
        Assert.Equal(2, published.Version);
    }

    [Fact]
    public async Task Reschedule_throws_when_the_event_does_not_exist()
    {
        var handler = new RescheduleEventCommandHandler(_events, _publisher);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RescheduleEventCommand("missing", FarEnoughOut), CancellationToken.None));
    }

    /// <summary>
    /// A refused change must not reach the store and must not be announced — otherwise consumers act
    /// on something that never happened.
    /// </summary>
    [Fact]
    public async Task Reschedule_publishes_nothing_when_the_domain_refuses()
    {
        var @event = AnEvent();
        var handler = new RescheduleEventCommandHandler(_events, _publisher);

        await Assert.ThrowsAsync<EventsDomainException>(() =>
            handler.Handle(new RescheduleEventCommand(@event.Id, DateTime.UtcNow.AddDays(2)),
                CancellationToken.None));

        Assert.Empty(_events.Updated);
        Assert.Empty(_publisher.Published);
    }

    // --- Relocate ---

    [Fact]
    public async Task Relocate_snapshots_the_new_venue_and_announces_its_seats()
    {
        var @event = AnEvent();
        var destination = AVenue("B1", "B2");
        _venues.Seed(destination);

        await new RelocateEventCommandHandler(_events, _venues, _publisher)
            .Handle(new RelocateEventCommand(@event.Id, destination.Id), CancellationToken.None);

        Assert.Equal(destination.Id, @event.Venue.Id);

        var published = _publisher.PublishedSingle<EventRelocatedIntegrationEvent>();
        Assert.Equal(destination.Id, published.VenueId);
        Assert.Equal(["B1", "B2"], published.Seats);
        Assert.Equal(2, published.Version);
    }

    [Fact]
    public async Task Relocate_throws_when_the_destination_venue_does_not_exist()
    {
        var @event = AnEvent();
        var handler = new RelocateEventCommandHandler(_events, _venues, _publisher);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new RelocateEventCommand(@event.Id, "missing"), CancellationToken.None));

        Assert.Empty(_publisher.Published);
    }

    // --- Lineup ---

    [Fact]
    public async Task Lineup_change_replaces_the_performers_without_announcing_anything()
    {
        var @event = AnEvent();
        var replacement = APerformer();
        _performers.Seed(replacement);

        await new ChangeEventLineupCommandHandler(_events, _performers, _publisher)
            .Handle(new ChangeEventLineupCommand(@event.Id, [replacement.Id]), CancellationToken.None);

        Assert.Equal(replacement.Id, @event.Performers.Single().Id);

        // Tickets do not depend on who is performing, so there is deliberately no contract for this.
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task Lineup_change_throws_when_a_performer_does_not_exist()
    {
        var @event = AnEvent();
        var handler = new ChangeEventLineupCommandHandler(_events, _performers, _publisher);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new ChangeEventLineupCommand(@event.Id, ["missing"]), CancellationToken.None));
    }

    // --- Cancel ---

    [Fact]
    public async Task Cancel_marks_the_event_cancelled_and_announces_it()
    {
        var @event = AnEvent();

        await new CancelEventCommandHandler(_events, _publisher)
            .Handle(new CancelEventCommand(@event.Id), CancellationToken.None);

        Assert.Equal(EventStatus.Cancelled, @event.Status);

        var published = _publisher.PublishedSingle<EventCancelledIntegrationEvent>();
        Assert.Equal(@event.Id, published.EventId);
        Assert.Equal(2, published.Version);
    }

    /// <summary>
    /// Cancelling an already-cancelled event is the same request arriving twice. The aggregate makes
    /// it a no-op, so nothing is announced a second time.
    /// </summary>
    [Fact]
    public async Task Cancel_announces_nothing_the_second_time()
    {
        var @event = AnEvent();
        var handler = new CancelEventCommandHandler(_events, _publisher);
        await handler.Handle(new CancelEventCommand(@event.Id), CancellationToken.None);

        var publishedFirst = _publisher.Published.Count;

        await handler.Handle(new CancelEventCommand(@event.Id), CancellationToken.None);

        Assert.Equal(publishedFirst, _publisher.Published.Count);
    }

    [Fact]
    public async Task Cancel_throws_when_the_event_does_not_exist()
    {
        var handler = new CancelEventCommandHandler(_events, _publisher);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new CancelEventCommand("missing"), CancellationToken.None));
    }
}
