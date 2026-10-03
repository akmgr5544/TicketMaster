using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

// Money asked back for some of a booking's seats; the seats it covers are the booked tickets carrying its id. No
// amount means the whole booking — whatever was still paid when a relocation voided it.
public sealed class BookingRefund
{
    public Guid Id { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Currency { get; private set; }
    public BookingRefundStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private BookingRefund()
    {
    }

    internal BookingRefund(decimal? amount, string? currency)
    {
        Id = Guid.CreateVersion7();
        Amount = amount;
        Currency = currency;
        Status = BookingRefundStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public bool IsWholeBooking => Amount is null;

    // True only on the change, so the seats it covers are released once.
    internal bool Complete()
    {
        if (Status == BookingRefundStatus.Completed)
            return false;

        Status = BookingRefundStatus.Completed;
        return true;
    }
}
