namespace Bookings.Domain.Entities;

public sealed class BookedTicket
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    public long BookingId { get; set; }

    internal BookedTicket(long ticketId, long bookingId)
    {
        TicketId = ticketId;
        BookingId = bookingId;
    }
}