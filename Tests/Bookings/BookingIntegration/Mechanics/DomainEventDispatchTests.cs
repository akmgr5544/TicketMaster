using Bookings.Application.Exceptions;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Mechanics;

/// <summary>
/// The real dispatch chain against a real relational provider: save a booking, let the interceptor
/// publish its creation event, and let the real, DI-registered handler for that event load the
/// tickets, book them, and save again — a save that begins while the first one's completion callback
/// is still on the stack.
/// <para>
/// This is the production wiring, unmodified: <see cref="BookingDomainContext"/> comes straight from
/// the fixture's container, with the same interceptor and the same handler graph a command sent
/// through <c>ISender</c> would use. Fakes cannot answer the two questions that matter: whether EF
/// tolerates that nested save at all, and whether clearing domain events before publishing is what
/// stops the second save re-publishing them forever. <see cref="BookingCreatedPublishCounter"/> is a
/// second, counting handler the fixture registers alongside the real one, which is what lets the
/// tests below observe the publish count directly instead of inferring it from a side effect.
/// </para>
/// </summary>
public sealed class DomainEventDispatchTests : IntegrationTest
{
    public DomainEventDispatchTests(BookingsFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    /// The original bug: the status was set on an entity nobody saved, so it never reached the
    /// database. Asserted by reading the rows back through a fresh scope.
    /// </summary>
    [Fact]
    public async Task Booking_marks_its_tickets_booked_in_the_database()
    {
        var tickets = await Seed.TicketsAsync("event-1", "A1", "A2");
        var context = Act.GetRequiredService<BookingDomainContext>();
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var stored = await ReadAsync(db => db.Tickets.AsNoTracking().ToArrayAsync());
        Assert.All(stored, ticket => Assert.Equal(TicketStatus.Booked, ticket.Status));
    }

    /// <summary>
    /// The handler's own save re-enters the interceptor. Without clearing the events first, the same
    /// creation event would be published again from the still-tracked booking, and the handler would
    /// run again with it — recursion, not a duplicate delivery.
    /// <para>
    /// Asserted directly on <see cref="BookingCreatedPublishCounter"/> rather than on a side effect of
    /// the real handler: a broken clear-before-publish order recurses into the real handler trying to
    /// book the same ticket twice, and <see cref="Ticket.Book"/> happens to refuse that today — but
    /// that throw belongs to <c>Ticket</c>, not to this interceptor rule, and a more tolerant, future
    /// <c>Book</c> (in the early-return style <see cref="Ticket.Release"/> already uses) would make it
    /// silently succeed twice instead. Whatever happens on that path is caught and discarded so it
    /// cannot masquerade as this test's failure signal; the counter and the events collection below
    /// are what actually decide it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Publishes_the_creation_event_exactly_once()
    {
        var tickets = await Seed.TicketsAsync("event-1", "A1");
        var context = Act.GetRequiredService<BookingDomainContext>();
        var publishes = Act.GetRequiredService<BookingCreatedPublishCounter>();
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());

        context.Bookings.Add(booking);
        await Record.ExceptionAsync(() => context.SaveChangesAsync());

        Assert.Equal(1, publishes.Count);
        Assert.Empty(booking.DomainEvents);
    }

    /// <summary>
    /// A second save of an aggregate whose events have already been dispatched must publish nothing —
    /// there is nothing left on <see cref="Booking.DomainEvents"/> for the interceptor to find. Two
    /// saves against a counter that only a single publish would leave at 1 is what actually
    /// distinguishes this from <see cref="Publishes_the_creation_event_exactly_once"/>; asserting only
    /// that <c>DomainEvents</c> is empty here would already hold true after the first save alone.
    /// </summary>
    [Fact]
    public async Task Saving_again_republishes_nothing()
    {
        var tickets = await Seed.TicketsAsync("event-1", "A1");
        var context = Act.GetRequiredService<BookingDomainContext>();
        var publishes = Act.GetRequiredService<BookingCreatedPublishCounter>();
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        await context.SaveChangesAsync();

        Assert.Equal(1, publishes.Count);
    }

    /// <summary>
    /// A booking that points at a ticket which no longer exists is not something to paper over: the
    /// throw rolls back the booking that raised this event rather than leaving one behind whose seats
    /// were never really taken.
    /// </summary>
    [Fact]
    public async Task Refuses_when_a_ticket_the_booking_covers_has_gone()
    {
        var tickets = await Seed.TicketsAsync("event-1", "A1");
        var context = Act.GetRequiredService<BookingDomainContext>();
        var booking = Booking.Create("user-1", BookingStatus.Booked, [tickets[0].Id, long.MaxValue]);

        context.Bookings.Add(booking);

        await Assert.ThrowsAsync<BookingsApplicationException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// Booking a seat that is already taken or cancelled is a conflict, not something to overwrite.
    /// The ticket itself refuses it; this only confirms the handler lets that surface.
    /// </summary>
    [Fact]
    public async Task Refuses_when_a_ticket_cannot_be_booked()
    {
        var tickets = await Seed.TicketsAsync("event-1", "A1");
        var context = Act.GetRequiredService<BookingDomainContext>();

        var ticket = await context.Tickets.SingleAsync(t => t.Id == tickets[0].Id);
        ticket.Cancel(eventVersion: 1);
        await context.SaveChangesAsync();

        var booking = Booking.Create("user-1", BookingStatus.Booked, [tickets[0].Id]);
        context.Bookings.Add(booking);

        await Assert.ThrowsAsync<BookingsDomainException>(() => context.SaveChangesAsync());
    }
}
