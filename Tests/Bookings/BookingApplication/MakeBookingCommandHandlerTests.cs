using Bookings.Application.CommandHandlers;
using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Locking;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using BookingApplication.Fakes;

namespace BookingApplication;

/// <summary>
/// Turning a reservation into a booking. The checks here are all-or-nothing on purpose: a request for
/// two seats whose reservation has half expired, or whose tickets are no longer both available, is not
/// something to quietly book the remainder of — the user reserved a pair and is paying for a pair.
/// </summary>
public class MakeBookingCommandHandlerTests
{
    private const string EventId = "event-1";
    private const string UserId = "user-1";

    private static readonly DateTime StartDate = new(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc);

    private readonly FakeBookingRepository _bookings = new();
    private readonly FakeTicketsRepository _tickets = new();
    private readonly FakeCacheService _cache = new();
    private readonly FakeAfterCommitQueue _afterCommit = new();

    private static Ticket ATicket(long id, string seat)
    {
        var ticket = new Ticket(seat, "venue-1", EventId, StartDate);
        ticket.Id = id;
        return ticket;
    }

    private void Reserved(long ticketId, string userId = UserId, string eventId = EventId) =>
        _cache.Seed(ReservationKeys.Reservation(ticketId), new ReserveTicketDto(ticketId, eventId, userId));

    private Task Book(params long[] ticketIds) =>
        new MakeBookingCommandHandler(_bookings, _tickets, _cache, _afterCommit)
            .Handle(new MakeBookingCommand(UserId, EventId, BookingStatus.Booked, ticketIds),
                CancellationToken.None);

    [Fact]
    public async Task Creates_a_booking_for_the_reserved_tickets()
    {
        _tickets.Seed(ATicket(7, "A1"), ATicket(9, "A2"));
        Reserved(7);
        Reserved(9);

        await Book(7, 9);

        var booking = Assert.Single(_bookings.Bookings);
        Assert.Equal(UserId, booking.UserId);
        Assert.Equal([7L, 9L], booking.BookedTickets.Select(x => x.TicketId).Order());
        Assert.Equal(1, _bookings.SaveCount);
    }

    /// <summary>
    /// The booking announces itself so the tickets get marked as booked. Without this the seats stay
    /// available and the booking means nothing.
    /// </summary>
    [Fact]
    public async Task Booking_announces_itself_so_its_tickets_can_be_booked()
    {
        _tickets.Seed(ATicket(7, "A1"));
        Reserved(7);

        await Book(7);

        var booking = Assert.Single(_bookings.Bookings);
        var created = Assert.IsType<BookingCreatedDomainEvent>(Assert.Single(booking.DomainEvents));
        Assert.Equal([7L], created.TicketIds);
    }

    /// <summary>
    /// Looked up under the namespaced key. A bare ticket id would collide with anything else sharing
    /// the Redis instance, and the reservation this reads would be whatever wrote last.
    /// </summary>
    [Fact]
    public async Task Reads_the_reservation_under_its_namespaced_key()
    {
        _tickets.Seed(ATicket(7, "A1"));
        _cache.Seed("7", new ReserveTicketDto(7, EventId, UserId));

        await Assert.ThrowsAsync<BookingException>(() => Book(7));
    }

    [Fact]
    public async Task Refuses_when_only_some_of_the_tickets_are_still_reserved()
    {
        _tickets.Seed(ATicket(7, "A1"), ATicket(9, "A2"));
        Reserved(7);

        await Assert.ThrowsAsync<BookingException>(() => Book(7, 9));
        Assert.Empty(_bookings.Bookings);
    }

    [Fact]
    public async Task Refuses_when_only_some_of_the_tickets_are_still_available()
    {
        _tickets.Seed(ATicket(7, "A1"));
        Reserved(7);
        Reserved(9);

        await Assert.ThrowsAsync<BookingException>(() => Book(7, 9));
        Assert.Empty(_bookings.Bookings);
    }

    [Fact]
    public async Task Refuses_a_reservation_belonging_to_somebody_else()
    {
        _tickets.Seed(ATicket(7, "A1"));
        Reserved(7, userId: "other-user");

        await Assert.ThrowsAsync<BookingException>(() => Book(7));
    }

    [Fact]
    public async Task Refuses_a_reservation_made_for_a_different_event()
    {
        _tickets.Seed(ATicket(7, "A1"));
        Reserved(7, eventId: "event-2");

        await Assert.ThrowsAsync<BookingException>(() => Book(7));
    }

    [Fact]
    public async Task Refuses_more_tickets_than_allowed()
    {
        await Assert.ThrowsAsync<BookingException>(() => Book(7, 9, 11));
    }

    [Fact]
    public async Task Refuses_a_request_with_no_tickets()
    {
        await Assert.ThrowsAsync<BookingException>(() => Book());
    }

    // --- Handing the reservation over ---

    /// <summary>
    /// Once the booking exists the seats are held by the tickets' own status, which is durable where
    /// the reservation was not, so the reservation is finished with.
    /// </summary>
    [Fact]
    public async Task Deletes_the_reservation_once_the_transaction_commits()
    {
        _tickets.Seed(ATicket(7, "A1"));
        Reserved(7);

        await Book(7);
        await _afterCommit.RunAllAsync(CancellationToken.None);

        Assert.Empty(_cache.Keys);
    }

    /// <summary>
    /// But not before. The handler runs inside the transaction and Redis does not roll back with it —
    /// deleting here would leave a user whose booking failed without the reservation they still hold.
    /// </summary>
    [Fact]
    public async Task Keeps_the_reservation_until_the_transaction_commits()
    {
        _tickets.Seed(ATicket(7, "A1"));
        Reserved(7);

        await Book(7);

        Assert.Equal([ReservationKeys.Reservation(7)], _cache.Keys);
    }

    [Fact]
    public async Task Queues_no_cleanup_when_the_booking_is_refused()
    {
        _tickets.Seed(ATicket(7, "A1"));

        await Assert.ThrowsAsync<BookingException>(() => Book(7));

        Assert.False(_afterCommit.HasWork);
    }
}
