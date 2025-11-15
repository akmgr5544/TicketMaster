using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public class Booking
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public BookingStatus Status { get; set; }
    public List<BookingHistory> BookingHistories { get; set; }
    public List<BookedTicket> BookedTickets { get; set; }
}