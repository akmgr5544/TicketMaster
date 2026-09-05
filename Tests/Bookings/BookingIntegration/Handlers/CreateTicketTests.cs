using Bookings.Application.Commands.Tickets;
using Bookings.Application.Exceptions;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Handlers;

/// <summary>
/// The admin repair path. It validates against the catalogue rather than against Bookings' own
/// tickets, because the local replica is what the admin is using this command to fix.
/// </summary>
public sealed class CreateTicketTests : IntegrationTest
{
    private const string EventId = "evt-1";
    private const string Venue = "venue-1";

    public CreateTicketTests(BookingsFixture fixture) : base(fixture)
    {
    }

    private StubEventsService Catalogue => Act.GetRequiredService<StubEventsService>();

    [Fact]
    public async Task Creates_a_seat_the_catalogue_has()
    {
        Catalogue.Knows(EventId, Venue, "A1", "A2");
        var eventDate = Seed.Soon;

        await Sender.Send(new CreateTicketCommand(EventId, Venue, "A2", eventDate));

        var stored = await ReadAsync(context => context.Tickets
            .SingleAsync(t => t.EventId == EventId && t.Seat == "A2"));

        Assert.Equal(Venue, stored.VenueId);
        Assert.Equal(eventDate, stored.EventDate);
        Assert.Equal(TicketStatus.None, stored.Status);
    }

    /// <summary>
    /// The reason this is an RPC. A lost or unprocessed EventCreated leaves Bookings with no ticket
    /// for the event at all — which is exactly when an admin reaches for this command, so validating
    /// against the replica would refuse the repair because the thing needing repair is missing.
    /// </summary>
    [Fact]
    public async Task Creates_a_seat_for_an_event_the_replica_has_never_heard_of()
    {
        Catalogue.Knows(EventId, Venue, "A1");

        await Sender.Send(new CreateTicketCommand(EventId, Venue, "A1", Seed.Soon));

        var stored = await ReadAsync(context => context.Tickets
            .SingleAsync(t => t.EventId == EventId && t.Seat == "A1"));

        Assert.Equal(Venue, stored.VenueId);
    }

    /// <summary>
    /// Starting at 0 beside siblings at 5 would make the repaired seat the only one a redelivered
    /// older change can still touch.
    /// </summary>
    [Fact]
    public async Task Repairs_a_seat_at_the_version_its_siblings_are_at()
    {
        Catalogue.Knows(EventId, Venue, "A1", "A2");
        await Seed.TicketsAsync(EventId, Seed.Soon, eventVersion: 5, "A1");

        await Sender.Send(new CreateTicketCommand(EventId, Venue, "A2", Seed.Soon));

        var stored = await ReadAsync(context => context.Tickets
            .SingleAsync(t => t.EventId == EventId && t.Seat == "A2"));

        Assert.Equal(5, stored.EventVersion);
    }

    [Fact]
    public async Task Refuses_an_event_the_catalogue_does_not_have()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new CreateTicketCommand("evt-unknown", Venue, "A1", Seed.Soon)));
    }

    [Fact]
    public async Task Refuses_a_venue_the_catalogue_disagrees_with()
    {
        Catalogue.Knows(EventId, Venue, "A1");

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new CreateTicketCommand(EventId, "venue-somewhere-else", "A1", Seed.Soon)));

        Assert.Equal(0, await ReadAsync(context => context.Tickets.CountAsync()));
    }

    [Fact]
    public async Task Refuses_a_seat_the_venue_does_not_have()
    {
        Catalogue.Knows(EventId, Venue, "A1", "A2");

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new CreateTicketCommand(EventId, Venue, "Z9", Seed.Soon)));

        Assert.Equal(0, await ReadAsync(context => context.Tickets.CountAsync()));
    }

    [Fact]
    public async Task Refuses_a_seat_that_already_has_a_live_ticket()
    {
        Catalogue.Knows(EventId, Venue, "A1");
        await Seed.TicketsAsync(EventId, "A1");

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new CreateTicketCommand(EventId, Venue, "A1", Seed.Soon)));

        var count = await ReadAsync(context => context.Tickets
            .CountAsync(t => t.EventId == EventId && t.Seat == "A1"));
        Assert.Equal(1, count);
    }

    /// <summary>
    /// Reconciliation treats a cancelled seat as uncovered and will re-add it, so the manual path has
    /// to agree or the two disagree about what a cancelled seat means.
    /// </summary>
    [Fact]
    public async Task Allows_a_seat_whose_only_ticket_was_cancelled()
    {
        Catalogue.Knows(EventId, Venue, "A1");
        await Seed.CancelledTicketsAsync(EventId, "A1");

        await Sender.Send(new CreateTicketCommand(EventId, Venue, "A1", Seed.Soon));

        var live = await ReadAsync(context => context.Tickets
            .CountAsync(t => t.EventId == EventId && t.Seat == "A1" && t.Status == TicketStatus.None));
        Assert.Equal(1, live);
    }

    [Fact]
    public async Task Surfaces_an_unreachable_catalogue_rather_than_guessing()
    {
        Catalogue.Fails = new EventsUnavailableException("Events is down.");

        await Assert.ThrowsAsync<EventsUnavailableException>(() =>
            Sender.Send(new CreateTicketCommand(EventId, Venue, "A1", Seed.Soon)));

        Assert.Equal(0, await ReadAsync(context => context.Tickets.CountAsync()));
    }
}
