namespace Bookings.Domain.Entities;

public sealed class BookedTicket
{
    public long Id { get; set; }
    public long TicketId { get; set; }

    internal BookedTicket(long ticketId)
    {
        TicketId = ticketId;
    }
}