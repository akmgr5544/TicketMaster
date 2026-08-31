using Bookings.Application.Commands.Payments;
using Bookings.Application.Exceptions;
using Bookings.Application.Queries;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingIntegration.Handlers;

/// <summary>
/// A customer reading and cancelling their own bookings.
/// <para>
/// The recurring assertion is that scoping by caller <i>is</i> the authorization check: somebody
/// else's booking has to answer exactly as a nonexistent one does, because distinguishing them would
/// confirm that a given id exists to someone with no business knowing. <c>GetBookingQuery</c> and
/// <c>ListBookingsQuery</c> scope in the SQL itself (<c>FindForUserAsync</c> / <c>ListForUserAsync</c>);
/// <c>CancelBookingCommand</c> instead loads by id and compares <c>UserId</c> in memory, so it reaches
/// the same 404 through a different path.
/// </para>
/// </summary>
public sealed class CustomerBookingTests : IntegrationTest
{
    private const string Owner = "user-1";
    private const string Stranger = "user-2";

    public CustomerBookingTests(BookingsFixture fixture) : base(fixture)
    {
    }

    // --- Reading one ---

    [Fact]
    public async Task Returns_the_caller_s_own_booking()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var booking = await Seed.BookingAsync(Owner, tickets[0].Id, tickets[1].Id);

        var dto = await Sender.Send(new GetBookingQuery(booking.Id, Owner));

        Assert.Equal(booking.Id, dto.Id);
        Assert.Equal(nameof(BookingStatus.Booked), dto.Status);
        Assert.Equal(new[] { tickets[0].Id, tickets[1].Id }.Order(), dto.TicketIds.Order());
    }

    [Fact]
    public async Task Reports_the_history_of_the_booking()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync(Owner, tickets[0].Id);

        var dto = await Sender.Send(new GetBookingQuery(booking.Id, Owner));

        var history = Assert.Single(dto.History);
        Assert.Equal(nameof(BookingStatus.Booked), history.Status);
        Assert.Equal(1, history.TicketsCount);
    }

    [Fact]
    public async Task Somebody_else_s_booking_is_not_found()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync(Stranger, tickets[0].Id);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new GetBookingQuery(booking.Id, Owner)));
    }

    [Fact]
    public async Task A_booking_that_does_not_exist_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new GetBookingQuery(long.MaxValue, Owner)));
    }

    // --- Listing ---

    [Fact]
    public async Task Lists_only_the_caller_s_bookings()
    {
        var ownTickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var strangerTickets = await Seed.TicketsAsync("evt-2", "A1");

        await Seed.BookingAsync(Owner, ownTickets[0].Id);
        var strangerBooking = await Seed.BookingAsync(Stranger, strangerTickets[0].Id);
        await Seed.BookingAsync(Owner, ownTickets[1].Id);

        var page = await Sender.Send(new ListBookingsQuery(Owner, Page: 1, PageSize: 25));

        Assert.Equal(2, page.Length);
        Assert.All(page, dto => Assert.NotEqual(strangerBooking.Id, dto.Id));
    }

    /// <summary>
    /// Newest first. <c>Booking</c> has no timestamp, so the key is the only proxy for age.
    /// </summary>
    [Fact]
    public async Task Lists_the_newest_booking_first()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var first = await Seed.BookingAsync(Owner, tickets[0].Id);
        var second = await Seed.BookingAsync(Owner, tickets[1].Id);

        var page = await Sender.Send(new ListBookingsQuery(Owner, Page: 1, PageSize: 25));

        Assert.Equal([second.Id, first.Id], page.Select(dto => dto.Id));
    }

    /// <summary>
    /// Real <c>ORDER BY / OFFSET / LIMIT</c> can break in ways an in-memory list cannot: a row on two
    /// pages, a row on none, or ordering that only happens to look right. The property here is that the
    /// concatenation of every page equals the full set in reverse creation order.
    /// </summary>
    [Fact]
    public async Task Pages_through_the_caller_s_bookings()
    {
        var made = new List<long>();

        for (var i = 0; i < 5; i++)
        {
            var tickets = await Seed.TicketsAsync($"evt-{i}", "A1");
            var booking = await Seed.BookingAsync(Owner, tickets[0].Id);
            made.Add(booking.Id);
        }

        var first = await Sender.Send(new ListBookingsQuery(Owner, Page: 1, PageSize: 2));
        var second = await Sender.Send(new ListBookingsQuery(Owner, Page: 2, PageSize: 2));
        var third = await Sender.Send(new ListBookingsQuery(Owner, Page: 3, PageSize: 2));

        Assert.Equal(2, first.Length);
        Assert.Equal(2, second.Length);
        Assert.Single(third);

        // Newest first, and no row appears on two pages.
        var returned = first.Concat(second).Concat(third).Select(b => b.Id).ToArray();
        Assert.Equal(made.AsEnumerable().Reverse(), returned);
    }

    [Fact]
    public async Task Lists_nothing_for_a_caller_with_no_bookings()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.BookingAsync(Stranger, tickets[0].Id);

        var page = await Sender.Send(new ListBookingsQuery(Owner, Page: 1, PageSize: 25));

        Assert.Empty(page);
    }

    // --- Cancelling ---

    [Fact]
    public async Task Cancels_the_caller_s_own_booking()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var booking = await Seed.BookingAsync(Owner, tickets[0].Id, tickets[1].Id);

        await Sender.Send(new CancelBookingCommand(booking.Id, Owner));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking.Id));
        Assert.Equal(BookingStatus.Cancelled, stored.Status);
    }

    /// <summary>
    /// The seats go back through the real domain-event chain: <c>Cancel()</c> raises
    /// <c>BookingCancelledDomainEvent</c>, whose handler releases the tickets. Reading through a fresh
    /// scope confirms the release actually reached the database rather than just the change tracker.
    /// </summary>
    [Fact]
    public async Task Cancelling_announces_the_tickets_to_release()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();
        var booking = await Seed.BookingAsync(Owner, ids);

        await Sender.Send(new CancelBookingCommand(booking.Id, Owner));

        var stored = await ReadAsync(context =>
            context.Tickets.Where(t => ids.Contains(t.Id)).ToArrayAsync());

        Assert.All(stored, ticket => Assert.Equal(TicketStatus.None, ticket.Status));
    }

    /// <summary>
    /// Unlike the queries, <c>CancelBookingCommandHandler</c> loads by id alone and compares
    /// <c>UserId</c> in memory, so this covers a different code path to the same 404.
    /// </summary>
    [Fact]
    public async Task Refuses_to_cancel_somebody_else_s_booking()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync(Stranger, tickets[0].Id);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new CancelBookingCommand(booking.Id, Owner)));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking.Id));
        Assert.Equal(BookingStatus.Booked, stored.Status);
    }

    [Fact]
    public async Task Refuses_to_cancel_a_booking_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new CancelBookingCommand(long.MaxValue, Owner)));
    }

    /// <summary>
    /// Cancelling a paid booking would be a refund, which this service does not do. Paid state is
    /// arranged through <c>ConfirmBookingCommand</c>, not by mutating the entity, so the aggregate's own
    /// refusal is what is under test.
    /// </summary>
    [Fact]
    public async Task Refuses_to_cancel_a_booking_that_has_been_paid_for()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync(Owner, tickets[0].Id);
        await Sender.Send(new ConfirmBookingCommand(booking.Id));

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new CancelBookingCommand(booking.Id, Owner)));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking.Id));
        Assert.Equal(BookingStatus.Payed, stored.Status);
    }
}
