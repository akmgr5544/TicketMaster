using Bookings.Application.EventSync;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using BookingApplication.Fakes;

namespace BookingApplication;

/// <summary>
/// The consumer side of the catalogue's integration events. Two properties matter throughout and are
/// asserted deliberately: applying a message twice must land in the same place, and a message that is
/// not newer than what has already been applied must change nothing.
/// </summary>
public class EventSyncHandlerTests
{
    private const string EventId = "event-1";
    private const string OldVenue = "venue-1";
    private const string NewVenue = "venue-2";

    private static readonly DateTime StartDate = new(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc);

    private readonly FakeTicketsRepository _tickets = new();

    private static Ticket ATicket(string seat, long eventVersion = 1) =>
        new(seat, OldVenue, EventId, StartDate, eventVersion);

    // --- Reschedule ---

    [Fact]
    public async Task Reschedule_moves_every_ticket_for_the_event()
    {
        _tickets.Seed(ATicket("A1"), ATicket("A2"));
        var newDate = StartDate.AddDays(30);

        await new RescheduleEventTicketsCommandHandler(_tickets)
            .Handle(new RescheduleEventTicketsCommand(EventId, 2, newDate), CancellationToken.None);

        Assert.All(_tickets.Tickets, ticket =>
        {
            Assert.Equal(newDate, ticket.EventDate);
            Assert.Equal(2, ticket.EventVersion);
        });
    }

    [Fact]
    public async Task Reschedule_leaves_other_events_alone()
    {
        var other = new Ticket("A1", OldVenue, "event-2", StartDate, 1);
        _tickets.Seed(ATicket("A1"), other);

        await new RescheduleEventTicketsCommandHandler(_tickets)
            .Handle(new RescheduleEventTicketsCommand(EventId, 2, StartDate.AddDays(30)), CancellationToken.None);

        Assert.Equal(StartDate, other.EventDate);
    }

    [Fact]
    public async Task Reschedule_ignores_a_message_that_is_not_newer()
    {
        _tickets.Seed(ATicket("A1", eventVersion: 5));

        await new RescheduleEventTicketsCommandHandler(_tickets)
            .Handle(new RescheduleEventTicketsCommand(EventId, 4, StartDate.AddDays(30)), CancellationToken.None);

        Assert.Equal(StartDate, _tickets.Tickets.Single().EventDate);
    }

    // --- Cancel ---

    [Fact]
    public async Task Cancel_cancels_every_ticket_for_the_event()
    {
        _tickets.Seed(ATicket("A1"), ATicket("A2"));

        await new CancelEventTicketsCommandHandler(_tickets)
            .Handle(new CancelEventTicketsCommand(EventId, 2), CancellationToken.None);

        Assert.All(_tickets.Tickets, ticket => Assert.Equal(TicketStatus.Cancelled, ticket.Status));
    }

    [Fact]
    public async Task Cancel_applied_twice_is_the_same_as_once()
    {
        _tickets.Seed(ATicket("A1"));
        var handler = new CancelEventTicketsCommandHandler(_tickets);
        var command = new CancelEventTicketsCommand(EventId, 2);

        await handler.Handle(command, CancellationToken.None);
        await handler.Handle(command, CancellationToken.None);

        var ticket = _tickets.Tickets.Single();
        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
        Assert.Equal(2, ticket.EventVersion);
    }

    // --- Venue reconcile ---

    [Fact]
    public async Task Reconcile_cancels_tickets_for_seats_the_new_venue_does_not_have()
    {
        _tickets.Seed(ATicket("A1"), ATicket("A2"));

        await Reconcile(version: 2, seats: ["A1"]);

        var gone = _tickets.Tickets.Single(t => t.Seat == "A2");
        Assert.Equal(TicketStatus.Cancelled, gone.Status);
    }

    [Fact]
    public async Task Reconcile_moves_tickets_for_seats_that_survive()
    {
        _tickets.Seed(ATicket("A1"));

        await Reconcile(version: 2, seats: ["A1", "B1"]);

        var kept = _tickets.Tickets.Single(t => t.Seat == "A1");
        Assert.Equal(NewVenue, kept.VenueId);
        Assert.Equal(TicketStatus.None, kept.Status);
    }

    [Fact]
    public async Task Reconcile_creates_tickets_for_seats_that_are_new()
    {
        _tickets.Seed(ATicket("A1"));

        await Reconcile(version: 2, seats: ["A1", "B1"]);

        var added = _tickets.Tickets.Single(t => t.Seat == "B1");
        Assert.Equal(NewVenue, added.VenueId);
        Assert.Equal(EventId, added.EventId);
        Assert.Equal(StartDate, added.EventDate);
        Assert.Equal(2, added.EventVersion);
        Assert.Equal(TicketStatus.None, added.Status);
    }

    /// <summary>
    /// The whole point of carrying the resulting seat set rather than a delta: redelivery is a no-op.
    /// </summary>
    [Fact]
    public async Task Reconcile_applied_twice_does_not_duplicate_tickets()
    {
        _tickets.Seed(ATicket("A1"));

        await Reconcile(version: 2, seats: ["A1", "B1"]);
        await Reconcile(version: 2, seats: ["A1", "B1"]);

        Assert.Equal(2, _tickets.Tickets.Count);
    }

    /// <summary>
    /// The case per-ticket guards cannot catch on their own: a stale message that would *add* tickets
    /// has no existing ticket to compare against, so the handler has to reject the whole message.
    /// </summary>
    [Fact]
    public async Task Reconcile_ignores_a_message_that_is_not_newer()
    {
        _tickets.Seed(ATicket("A1", eventVersion: 5));

        await Reconcile(version: 4, seats: ["B1"]);

        var ticket = _tickets.Tickets.Single();
        Assert.Equal(OldVenue, ticket.VenueId);
        Assert.Equal(TicketStatus.None, ticket.Status);
    }

    /// <summary>
    /// With no outbox in Events, the creation message can be lost outright. A later relocation still
    /// carries the full seat set, so it repairs the gap rather than leaving the event ticketless.
    /// </summary>
    [Fact]
    public async Task Reconcile_creates_the_whole_set_when_no_tickets_exist_yet()
    {
        await Reconcile(version: 2, seats: ["B1", "B2"]);

        Assert.Equal(2, _tickets.Tickets.Count);
        Assert.All(_tickets.Tickets, ticket => Assert.Equal(NewVenue, ticket.VenueId));
    }

    private Task Reconcile(long version, string[] seats) =>
        new ReconcileEventVenueCommandHandler(_tickets).Handle(
            new ReconcileEventVenueCommand(EventId, version, NewVenue, StartDate, seats),
            CancellationToken.None);
}
