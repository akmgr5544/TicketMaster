using Bookings.Application.Commands;
using Bookings.Application.Commands.Tickets;
using Bookings.Domain.Enums;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingIntegration.Handlers;

/// <summary>
/// The consumer side of the catalogue's integration events. Two properties matter throughout and are
/// asserted deliberately: applying a message twice must land in the same place, and a message that is
/// not newer than what has already been applied must change nothing.
/// <para>
/// All three commands are <see cref="Bookings.Domain.Abstractions.ITransactionalRequest"/>, so each
/// <c>Sender.Send</c> below runs inside its own real transaction against Postgres, with no unique
/// constraint on <c>Tickets</c> to paper over a genuine duplicate row.
/// </para>
/// </summary>
public sealed class EventSyncTests : IntegrationTest
{
    private const string EventId = "evt-1";
    private const string NewVenue = "venue-2";

    public EventSyncTests(BookingsFixture fixture) : base(fixture)
    {
    }

    // --- Reschedule ---

    [Fact]
    public async Task Reschedule_moves_every_ticket_for_the_event()
    {
        var eventDate = Seed.Soon;
        await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1", "A2");
        var newDate = eventDate.AddDays(30);

        await Sender.Send(new RescheduleEventTicketsCommand(EventId, 2, newDate));

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId)
            .ToArrayAsync());

        Assert.All(stored, ticket =>
        {
            Assert.Equal(newDate, ticket.EventDate);
            Assert.Equal(2, ticket.EventVersion);
        });
    }

    [Fact]
    public async Task Reschedule_leaves_other_events_alone()
    {
        var eventDate = Seed.Soon;
        await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1");
        var other = await Seed.TicketsAsync("evt-2", eventDate, eventVersion: 1, "A1");

        await Sender.Send(new RescheduleEventTicketsCommand(EventId, 2, eventDate.AddDays(30)));

        var stored = await ReadAsync(context => context.Tickets.SingleAsync(t => t.Id == other[0].Id));
        Assert.Equal(eventDate, stored.EventDate);
    }

    [Fact]
    public async Task Reschedule_ignores_a_message_that_is_not_newer()
    {
        var eventDate = Seed.Soon;
        var tickets = await Seed.TicketsAsync(EventId, eventDate, eventVersion: 5, "A1");

        await Sender.Send(new RescheduleEventTicketsCommand(EventId, 4, eventDate.AddDays(30)));

        var stored = await ReadAsync(context => context.Tickets.SingleAsync(t => t.Id == tickets[0].Id));
        Assert.Equal(eventDate, stored.EventDate);
    }

    // --- Cancel ---

    [Fact]
    public async Task Cancel_cancels_every_ticket_for_the_event()
    {
        var eventDate = Seed.Soon;
        await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1", "A2");

        await Sender.Send(new CancelEventTicketsCommand(EventId, 2));

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId)
            .ToArrayAsync());
        Assert.All(stored, ticket => Assert.Equal(TicketStatus.Cancelled, ticket.Status));
    }

    [Fact]
    public async Task Cancel_applied_twice_is_the_same_as_once()
    {
        var eventDate = Seed.Soon;
        await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1");
        var command = new CancelEventTicketsCommand(EventId, 2);

        await Sender.Send(command);
        await Sender.Send(command);

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId)
            .ToArrayAsync());
        var ticket = Assert.Single(stored);
        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
        Assert.Equal(2, ticket.EventVersion);
    }

    // --- Venue reconcile ---

    /// <summary>
    /// A cancelled ticket does not count as covering its seat. Once A2 has been told its ticket is
    /// void, a later reconcile that brings the seat back has to hand it a fresh ticket rather than
    /// resurrect the cancelled one — so this is the one case where two rows for the same seat, one
    /// cancelled and one active, is the correct outcome rather than a duplicate bug.
    /// </summary>
    [Fact]
    public async Task Reconcile_cancels_tickets_for_seats_the_new_venue_does_not_have()
    {
        var eventDate = Seed.Soon;
        await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1", "A2");

        await Reconcile(eventDate, version: 2, seats: ["A1"]);
        await Reconcile(eventDate, version: 3, seats: ["A1", "A2"]);

        var seatA2 = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId && t.Seat == "A2")
            .ToArrayAsync());

        Assert.Equal(2, seatA2.Length);
        Assert.Single(seatA2, t => t.Status == TicketStatus.Cancelled);
        Assert.Single(seatA2, t => t.Status == TicketStatus.None);
    }

    [Fact]
    public async Task Reconcile_moves_tickets_for_seats_that_survive()
    {
        var eventDate = Seed.Soon;
        var tickets = await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1");

        await Reconcile(eventDate, version: 2, seats: ["A1", "B1"]);

        var stored = await ReadAsync(context => context.Tickets.SingleAsync(t => t.Id == tickets[0].Id));
        Assert.Equal(NewVenue, stored.VenueId);
        Assert.Equal(TicketStatus.None, stored.Status);
    }

    [Fact]
    public async Task Reconcile_creates_tickets_for_seats_that_are_new()
    {
        var eventDate = Seed.Soon;
        await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1");

        await Reconcile(eventDate, version: 2, seats: ["A1", "B1"]);

        var added = await ReadAsync(context => context.Tickets
            .SingleAsync(t => t.EventId == EventId && t.Seat == "B1"));

        Assert.Equal(NewVenue, added.VenueId);
        Assert.Equal(EventId, added.EventId);
        Assert.Equal(eventDate, added.EventDate);
        Assert.Equal(2, added.EventVersion);
        Assert.Equal(TicketStatus.None, added.Status);
    }

    /// <summary>
    /// The whole point of carrying the resulting seat set rather than a delta: redelivery is a no-op.
    /// Real Postgres has no unique constraint on <c>Tickets</c>, so a handler that re-created B1 on the
    /// second delivery would show up here as two rows for the same seat rather than being masked the
    /// way a fake keyed by id would mask it.
    /// </summary>
    [Fact]
    public async Task Reconcile_applied_twice_does_not_duplicate_tickets()
    {
        var eventDate = Seed.Soon;
        await Seed.TicketsAsync(EventId, eventDate, eventVersion: 1, "A1");
        var command = new ReconcileEventVenueCommand(EventId, 2, NewVenue, eventDate, ["A1", "B1"]);

        await Sender.Send(command);
        await Sender.Send(command);

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId)
            .ToArrayAsync());
        Assert.Equal(2, stored.Length);
    }

    /// <summary>
    /// The case per-ticket guards cannot catch on their own: a stale message that would *add* tickets
    /// has no existing ticket to compare against, so the handler has to reject the whole message.
    /// </summary>
    [Fact]
    public async Task Reconcile_ignores_a_message_that_is_not_newer()
    {
        var eventDate = Seed.Soon;
        var tickets = await Seed.TicketsAsync(EventId, eventDate, eventVersion: 5, "A1");

        await Reconcile(eventDate, version: 4, seats: ["B1"]);

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId)
            .ToArrayAsync());
        var ticket = Assert.Single(stored);
        Assert.Equal(tickets[0].VenueId, ticket.VenueId);
        Assert.Equal(TicketStatus.None, ticket.Status);
    }

    /// <summary>
    /// With no outbox in Events, the creation message can be lost outright. A later relocation still
    /// carries the full seat set, so it repairs the gap rather than leaving the event ticketless.
    /// </summary>
    [Fact]
    public async Task Reconcile_creates_the_whole_set_when_no_tickets_exist_yet()
    {
        await Reconcile(Seed.Soon, version: 2, seats: ["B1", "B2"]);

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId)
            .ToArrayAsync());
        Assert.Equal(2, stored.Length);
        Assert.All(stored, ticket => Assert.Equal(NewVenue, ticket.VenueId));
    }

    // --- Creation from the catalogue ---

    [Fact]
    public async Task Tickets_created_from_the_catalogue_carry_its_version()
    {
        await Sender.Send(new CreateTicketsBulkCommand(EventId, NewVenue, Seed.Soon, ["A1", "A2"], Version: 5));

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.EventId == EventId)
            .ToArrayAsync());

        Assert.All(stored, ticket => Assert.Equal(5, ticket.EventVersion));
    }

    /// <summary>
    /// Cancel and reschedule have no message-level guard, so a ticket created below the version its
    /// event is actually at would accept a redelivered older change that every other ticket rejects,
    /// leaving one event half-applied.
    /// </summary>
    [Fact]
    public async Task A_stale_change_cannot_touch_tickets_just_created_from_the_catalogue()
    {
        var eventDate = Seed.Soon;
        await Sender.Send(new CreateTicketsBulkCommand(EventId, NewVenue, eventDate, ["A1"], Version: 5));

        await Sender.Send(new RescheduleEventTicketsCommand(EventId, 3, eventDate.AddDays(30)));

        var stored = await ReadAsync(context => context.Tickets.SingleAsync(t => t.EventId == EventId));
        Assert.Equal(eventDate, stored.EventDate);
        Assert.Equal(5, stored.EventVersion);
    }

    private Task Reconcile(DateTime eventDate, long version, string[] seats) =>
        Sender.Send(new ReconcileEventVenueCommand(EventId, version, NewVenue, eventDate, seats));
}
