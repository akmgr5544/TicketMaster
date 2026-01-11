using Bookings.Domain.Abstractions;
using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public sealed class Booking : Entity, IAggregateRoot
{
    public long Id { get; init; }
    public string UserId { get; init; } = null!;
    public BookingStatus Status { get; init; }
    public List<BookingHistory> BookingHistories { get; init; }
    public List<BookedTicket> BookedTickets { get; init; }

    private Booking()
    {
        
        BookedTickets = [];
        BookingHistories = [];
    }

    private Booking(string userId,
        BookingStatus status) : this()
    {
        UserId = userId;
        Status = status;
    }

    public void AddBookedTicket(long bookedTicketId)
    {
        BookedTickets.Add(new BookedTicket(bookedTicketId));
    }

    public static Booking Create(string userId, BookingStatus status, int ticketCount)
    {
        var booking = new Booking(userId, status);
        booking.BookingHistories.Add(new BookingHistory(booking.Status, ticketCount));
        return booking;
    }
}