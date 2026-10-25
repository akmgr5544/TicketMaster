using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

// Which message got to a booking first: a payment request or its cancellation. The booking id is the key, so a request
// and a cancellation arriving together cannot each miss the other's write — the second insert fails, and its retry
// sees what won. A request that finds the booking claimed Cancelled opens no checkout.
public class BookingClaim
{
    public long BookingId { get; private set; }
    public BookingClaimStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private BookingClaim() { }

    private BookingClaim(long bookingId, BookingClaimStatus status)
    {
        BookingId = bookingId;
        Status = status;
        CreatedAt = DateTime.UtcNow;
    }

    public static BookingClaim Requested(long bookingId) => new(bookingId, BookingClaimStatus.Requested);

    public static BookingClaim Cancelled(long bookingId) => new(bookingId, BookingClaimStatus.Cancelled);
}
