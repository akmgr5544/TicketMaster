namespace Bookings.Domain.Entities;

public sealed class BookedTicket
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    // The refund giving this seat's money back, once one is asked for. A seat is refunded at most once.
    public Guid? RefundId { get; private set; }

    internal BookedTicket(long ticketId)
    {
        TicketId = ticketId;
    }

    internal void CoverBy(Guid refundId) => RefundId = refundId;
}
