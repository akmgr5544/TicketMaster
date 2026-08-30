using Bookings.Application.Commands.Bookings;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Extensions;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Handlers;

/// <summary>
/// Turning a reservation into a booking. The checks in <c>GetValidTicketIdsAsync</c> are all-or-nothing
/// on purpose: a request for two seats whose reservation has half expired, or whose tickets are no
/// longer both available, is not something to quietly book the remainder of — the user reserved a pair
/// and is paying for a pair.
/// <para>
/// <c>MakeBookingCommand</c> is <see cref="ITransactionalRequest"/>, so the two after-commit tests below
/// depend on a real transaction and a real, scoped domain-event interceptor: a rollback genuinely takes
/// the ticket write with it, and Redis genuinely does not roll back with either.
/// </para>
/// </summary>
public sealed class MakeBookingTests : IntegrationTest
{
    public MakeBookingTests(BookingsFixture fixture) : base(fixture)
    {
    }

    // --- Happy path ---

    [Fact]
    public async Task Creates_a_booking_for_the_reserved_tickets()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();
        await Seed.ReservationAsync("user-1", "evt-1", ids);

        var bookingId = await Sender.Send(new MakeBookingCommand("user-1", "evt-1", ids));

        Assert.True(bookingId > 0);

        var stored = await ReadAsync(context => context.Bookings
            .Include(b => b.BookedTickets)
            .SingleAsync(b => b.Id == bookingId));

        Assert.Equal("user-1", stored.UserId);
        Assert.Equal(BookingStatus.Booked, stored.Status);
        Assert.Equal(ids.Order(), stored.BookedTickets.Select(t => t.TicketId).Order());
    }

    /// <summary>
    /// The booking announces itself so its tickets get marked booked, via the real domain-event
    /// dispatch chain, and it has to actually reach the database rather than just the change tracker.
    /// </summary>
    [Fact]
    public async Task Booking_announces_itself_so_its_tickets_can_be_booked()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();
        await Seed.ReservationAsync("user-1", "evt-1", ids);

        await Sender.Send(new MakeBookingCommand("user-1", "evt-1", ids));

        // Read through a fresh scope: the interceptor's nested SaveChangesAsync is exactly the thing
        // that could look right in the change tracker and never reach the database.
        var stored = await ReadAsync(context =>
            context.Tickets.Where(t => ids.Contains(t.Id)).ToArrayAsync());

        Assert.All(stored, ticket => Assert.Equal(TicketStatus.Booked, ticket.Status));
    }

    /// <summary>
    /// A ticket that exists for the same event but was never part of this booking has to stay
    /// untouched. Nothing in the happy-path tests above seeds a ticket outside the booking, so a
    /// handler that booked every ticket for the event rather than only the ones it was given would
    /// still pass all of them.
    /// </summary>
    [Fact]
    public async Task Leaves_tickets_the_booking_does_not_cover_alone()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var untouched = await Seed.TicketsAsync("evt-1", "A2");
        await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

        await Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id]));

        var stored = await ReadAsync(context =>
            context.Tickets.SingleAsync(t => t.Id == untouched[0].Id));

        Assert.Equal(TicketStatus.None, stored.Status);
    }

    /// <summary>
    /// Looked up under the namespaced key. A bare ticket id would collide with anything else sharing
    /// the Redis instance, and the reservation this reads would be whatever wrote last.
    /// </summary>
    [Fact]
    public async Task Reads_the_reservation_under_its_namespaced_key()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");

        // Written under the bare ticket id rather than ReservationKeys.Reservation(id).
        await Cache.SetToCacheAsync(
        [
            new KeyValuePair<string, ReserveTicketDto>(
                tickets[0].Id.ToString(), new ReserveTicketDto(tickets[0].Id, "evt-1", "user-1"))
        ]);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id])));
    }

    // --- All-or-nothing on the reservation checks ---

    [Fact]
    public async Task Refuses_when_only_some_of_the_tickets_are_still_reserved()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", tickets.Select(t => t.Id).ToArray())));
    }

    [Fact]
    public async Task Refuses_when_only_some_of_the_tickets_are_still_available()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var missingId = long.MaxValue;
        await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id, missingId);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id, missingId])));
    }

    [Fact]
    public async Task Refuses_a_reservation_belonging_to_somebody_else()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.ReservationAsync("other-user", "evt-1", tickets[0].Id);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id])));
    }

    [Fact]
    public async Task Refuses_a_reservation_made_for_a_different_event()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.ReservationAsync("user-1", "evt-2", tickets[0].Id);

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id])));
    }

    [Fact]
    public async Task Refuses_more_tickets_than_allowed()
    {
        long[] ticketIds = [long.MaxValue, long.MaxValue - 1, long.MaxValue - 2];

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", ticketIds)));
    }

    [Fact]
    public async Task Refuses_a_request_with_no_tickets()
    {
        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", [])));
    }

    // --- Handing the reservation over ---

    /// <summary>
    /// Once the booking exists the seats are held by the tickets' own status, which is durable where
    /// the reservation was not, so the reservation is finished with.
    /// </summary>
    [Fact]
    public async Task Deletes_the_reservation_once_the_transaction_commits()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

        await Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id]));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
            [ReservationKeys.Reservation(tickets[0].Id)]);

        Assert.Empty(held);
    }

    /// <summary>
    /// A booking that fails after the reservation was read must leave the hold in place, so the user
    /// can try again. Redis does not roll back with the transaction, which is why the delete is queued
    /// on IAfterCommitQueue rather than done in the handler.
    /// <para>
    /// The handler's own last statement enqueues the cleanup, so nothing inside it can fail afterwards
    /// — a failure has to be injected after the handler returns and before the transaction commits.
    /// <see cref="AfterHandlerFailureSwitch"/> does exactly that: flipped on, it makes
    /// <see cref="AfterHandlerFailureBehavior{TRequest,TResponse}"/> throw once the handler has already
    /// run (and so has already enqueued the reservation delete), which
    /// <c>TransactionBehavior</c> catches, rolling back without ever draining the queue. That is the
    /// one arrangement that actually distinguishes "queued until commit" from "run inline": with the
    /// real handler this test is green, and if the enqueue were replaced with an inline
    /// <c>await _cacheService.RemoveAsync(...)</c> the delete would already have happened by the time
    /// this failure is injected, and the assertion below would fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Keeps_the_reservation_until_the_transaction_commits()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

        Act.GetRequiredService<AfterHandlerFailureSwitch>().ShouldFailAfterHandler = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id])));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
            [ReservationKeys.Reservation(tickets[0].Id)]);

        Assert.Single(held);
    }

    [Fact]
    public async Task Queues_no_cleanup_when_the_booking_is_refused()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id])));

        Assert.False(Act.GetRequiredService<IAfterCommitQueue>().HasWork);
    }
}
