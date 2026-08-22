using Bookings.Application.CommandHandlers;
using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Locking;
using Bookings.Domain.Entities;
using Bookings.Domain.Exceptions;
using BookingApplication.Fakes;

namespace BookingApplication;

/// <summary>
/// Reserving is what stops two people buying one seat, and it used to rest entirely on a single lock
/// taken under a constant name — every reservation in the service queued behind every other, and the
/// safety came from that queue rather than from anything about the seats. These cover the replacement:
/// a lock per ticket, taken in a fixed order, with the check and the write both inside those locks.
/// </summary>
public class ReserveTicketCommandHandlerTests
{
    private const string EventId = "event-1";
    private const string UserId = "user-1";

    private static readonly DateTime StartDate = new(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc);

    private readonly FakeCacheService _cache = new();
    private readonly FakeLockProvider _locks = new();
    private readonly FakeTicketsRepository _tickets = new();

    private static Ticket ATicket(long id, string seat, string eventId = EventId)
    {
        var ticket = new Ticket(seat, "venue-1", eventId, StartDate);
        ticket.Id = id;
        return ticket;
    }

    public ReserveTicketCommandHandlerTests()
    {
        // Available seats for the ids these tests reserve. A seat has to be real and for sale before
        // it can be held, so every successful case needs one.
        _tickets.Seed(ATicket(7, "A1"), ATicket(9, "A2"), ATicket(11, "A3"));
    }

    private Ticket Ticket(long id) => _tickets.Tickets.Single(t => t.Id == id);

    private Task Reserve(params long[] ticketIds) =>
        new ReserveTicketCommandHandler(_locks, _cache, _tickets)
            .Handle(new ReserveTicketCommand(UserId, EventId, ticketIds), CancellationToken.None);

    // --- Reserving ---

    [Fact]
    public async Task Reserves_every_ticket_it_was_given()
    {
        await Reserve(7, 9);

        Assert.Equal(
            [ReservationKeys.Reservation(7), ReservationKeys.Reservation(9)],
            _cache.Keys.Order());
    }

    [Fact]
    public async Task Records_who_reserved_the_ticket_and_for_which_event()
    {
        await Reserve(7);

        var reservation = Assert.Single(
            await _cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(7)]));
        Assert.Equal(new ReserveTicketDto(7, EventId, UserId), reservation);
    }

    [Fact]
    public async Task Holds_the_reservation_for_the_configured_time()
    {
        await Reserve(7);

        Assert.Equal(TimeSpan.FromMinutes(5), Assert.Single(_cache.Expirations));
    }

    // --- Contention ---

    [Fact]
    public async Task Refuses_a_ticket_somebody_else_has_already_reserved()
    {
        _cache.Seed(ReservationKeys.Reservation(7), new ReserveTicketDto(7, EventId, "other-user"));

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7));
    }

    /// <summary>
    /// All or nothing: a request for two seats where one is taken reserves neither, and leaves the
    /// seat it could have had free for somebody else.
    /// </summary>
    [Fact]
    public async Task Reserves_nothing_when_one_of_the_tickets_is_taken()
    {
        _cache.Seed(ReservationKeys.Reservation(9), new ReserveTicketDto(9, EventId, "other-user"));

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7, 9));

        Assert.Equal([ReservationKeys.Reservation(9)], _cache.Keys);
    }

    [Fact]
    public async Task Refuses_when_another_reservation_is_holding_the_ticket()
    {
        _locks.HoldElsewhere(ReservationKeys.Lock(7));

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7));
        Assert.Empty(_cache.Keys);
    }

    // --- Locking ---

    [Fact]
    public async Task Locks_each_ticket_rather_than_everything_at_once()
    {
        await Reserve(7, 9);

        Assert.Equal([ReservationKeys.Lock(7), ReservationKeys.Lock(9)], _locks.Acquired);
    }

    /// <summary>
    /// The deadlock guard, and the reason the order is not the caller's. Two requests overlapping on
    /// seats 7 and 9 must both take 7 first; if one took them in the order it was asked for, each
    /// could end up holding the lock the other is waiting on.
    /// </summary>
    [Fact]
    public async Task Takes_the_locks_in_ticket_order_not_the_order_it_was_asked_for()
    {
        await Reserve(9, 7);

        Assert.Equal([ReservationKeys.Lock(7), ReservationKeys.Lock(9)], _locks.Acquired);
    }

    [Fact]
    public async Task Releases_every_lock_it_took()
    {
        await Reserve(7, 9);

        Assert.Equal(
            [ReservationKeys.Lock(7), ReservationKeys.Lock(9)],
            _locks.Released.Order());
    }

    /// <summary>
    /// A lock taken before the unavailable one has to be released too. Leaving it behind strands a
    /// seat nobody is reserving.
    /// </summary>
    [Fact]
    public async Task Releases_the_locks_it_took_before_hitting_one_it_could_not_have()
    {
        _locks.HoldElsewhere(ReservationKeys.Lock(9));

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7, 9));

        Assert.Equal([ReservationKeys.Lock(7)], _locks.Released);
    }

    [Fact]
    public async Task Releases_its_locks_even_when_the_reservation_fails()
    {
        _cache.Seed(ReservationKeys.Reservation(9), new ReserveTicketDto(9, EventId, "other-user"));

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7, 9));

        Assert.Equal(
            [ReservationKeys.Lock(7), ReservationKeys.Lock(9)],
            _locks.Released.Order());
    }

    // --- Rejected requests ---

    [Fact]
    public async Task Refuses_a_request_with_no_tickets()
    {
        await Assert.ThrowsAsync<BookingsDomainException>(() => Reserve());
    }

    [Fact]
    public async Task Refuses_more_tickets_than_allowed()
    {
        await Assert.ThrowsAsync<BookingsDomainException>(() => Reserve(7, 9, 11));
    }

    /// <summary>
    /// Redis locks are not reentrant, so the same ticket twice would have the request waiting on a
    /// lock it is already holding and then failing as though somebody else held it.
    /// </summary>
    [Fact]
    public async Task Refuses_the_same_ticket_twice()
    {
        await Assert.ThrowsAsync<BookingsDomainException>(() => Reserve(7, 7));
        Assert.Empty(_locks.Acquired);
    }

    // --- The seat has to be real and for sale ---

    /// <summary>
    /// Reservation used to ask Redis whether the ticket was already reserved and nothing else, so a
    /// ticket id that had never existed reserved perfectly happily and failed at booking instead.
    /// </summary>
    [Fact]
    public async Task Refuses_a_ticket_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Reserve(404));
        Assert.Empty(_cache.Keys);
    }

    [Fact]
    public async Task Refuses_when_only_some_of_the_tickets_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Reserve(7, 404));
        Assert.Empty(_cache.Keys);
    }

    [Fact]
    public async Task Refuses_a_ticket_that_belongs_to_another_event()
    {
        _tickets.Seed(ATicket(21, "B1", eventId: "event-2"));

        await Assert.ThrowsAsync<BookingsDomainException>(() => Reserve(21));
        Assert.Empty(_cache.Keys);
    }

    /// <summary>
    /// Sold is sold, whether or not it has been paid for — paying does not change the ticket, it is
    /// already booked by then.
    /// </summary>
    [Fact]
    public async Task Refuses_a_ticket_that_is_already_sold()
    {
        Ticket(7).Book();

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7));
        Assert.Empty(_cache.Keys);
    }

    [Fact]
    public async Task Refuses_a_ticket_the_event_has_cancelled()
    {
        Ticket(7).Cancel(eventVersion: 2);

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7));
        Assert.Empty(_cache.Keys);
    }

    /// <summary>
    /// The other side of it: a seat released because its payment failed has to be holdable again, or a
    /// failed payment would take it out of circulation as surely as a successful one.
    /// </summary>
    [Fact]
    public async Task Allows_a_ticket_that_was_released_after_a_failed_payment()
    {
        Ticket(7).Book();
        Ticket(7).Release();

        await Reserve(7);

        Assert.Equal([ReservationKeys.Reservation(7)], _cache.Keys);
    }

    [Fact]
    public async Task Reserves_nothing_when_one_of_the_tickets_is_unavailable()
    {
        Ticket(9).Book();

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7, 9));

        Assert.Empty(_cache.Keys);
    }

    [Fact]
    public async Task Releases_its_locks_when_a_ticket_is_unavailable()
    {
        Ticket(9).Book();

        await Assert.ThrowsAsync<BookingsApplicationException>(() => Reserve(7, 9));

        Assert.Equal(
            [ReservationKeys.Lock(7), ReservationKeys.Lock(9)],
            _locks.Released.Order());
    }
}
