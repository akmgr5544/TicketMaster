using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public class Booking : RootEntity
{
    public long Id { get; set; }
    public string UserId { get; set; }
    public BookingStatus Status { get; set; }
    public List<BookingHistory> BookingHistories { get; set; }
    public List<BookedTicket> BookedTickets { get; set; }

    public Booking(string userId,
        BookingStatus status,
        List<BookedTicket> bookedTickets)
    {
        UserId = userId;
        Status = status;
        BookedTickets = bookedTickets;
        BookingHistories = [new BookingHistory(status, bookedTickets.Count)];
    }
}