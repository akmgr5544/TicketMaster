using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

// Money asked back for a booking; the seats it covers are the booked tickets carrying its id. Refunds are whole:
// everything paid goes back, so there is no amount.
public sealed class BookingRefund
{
    public Guid Id { get; private set; }
    public BookingRefundStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    internal BookingRefund()
    {
        Id = Guid.CreateVersion7();
        Status = BookingRefundStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    // True only on the change, so the seats it covers are released once.
    internal bool Complete()
    {
        if (Status == BookingRefundStatus.Completed)
            return false;

        Status = BookingRefundStatus.Completed;
        return true;
    }

    // True only on the change from Pending: a completed refund's seats were released already.
    internal bool Fail()
    {
        if (Status != BookingRefundStatus.Pending)
            return false;

        Status = BookingRefundStatus.Failed;
        return true;
    }
}
