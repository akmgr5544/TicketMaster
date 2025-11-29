using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public class BookingHistory
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public BookingStatus BookingStatus { get; set; }
    public int TicketsCount { get; set; }

    public BookingHistory(BookingStatus bookingStatus, int ticketsCount)
    {
        BookingStatus = bookingStatus;
        TicketsCount = ticketsCount;
    }
}