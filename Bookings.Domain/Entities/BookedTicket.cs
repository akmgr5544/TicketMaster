namespace Bookings.Domain.Entities;

public class BookedTicket
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    public long BookingId { get; set; }
}