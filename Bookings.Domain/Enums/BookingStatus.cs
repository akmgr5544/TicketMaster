namespace Bookings.Domain.Enums;

public enum BookingStatus
{
    None,
    InProgress,
    Cancelled,
    Booked,
    Payed,
    RefundPending,
    Refunded,
    // The refund failed at the PSP: the seats are back on sale and a person returns the money another way.
    RefundFailed
}