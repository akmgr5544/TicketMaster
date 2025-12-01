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

    public Booking(string userId,
        BookingStatus status)
    {
        UserId = userId;
        Status = status;
        BookedTickets = [];
        //TODO::
        BookingHistories = [];
    }

    public void AddBookedTicket(long bookedTicketId)
    {
        BookedTickets.Add(new BookedTicket(bookedTicketId, Id));
    }
}