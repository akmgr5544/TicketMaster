using Bookings.Domain.Abstractions;
using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public sealed class Booking : Entity, IAggregateRoot
{
    public long Id { get; set; }
    public string UserId { get; set; }
    public BookingStatus Status { get; set; }
    public List<BookingHistory> BookingHistories { get; set; }
    public List<BookedTicket> BookedTickets { get; set; }

    private Booking()
    {
    }

    private Booking(string userId,
        BookingStatus status)
    {
        UserId = userId;
        Status = status;
        BookedTickets = [];
        BookingHistories = [];
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