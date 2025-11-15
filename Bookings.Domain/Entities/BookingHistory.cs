using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public class BookingHistory
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public Booking? Booking { get; set; }
    public BookingStatus BookingStatus { get; set; }
    public int TicketsCount { get; set; }
}