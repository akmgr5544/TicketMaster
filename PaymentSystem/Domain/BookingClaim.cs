using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class BookingClaim
{
    public long BookingId { get; private set; }
    public BookingClaimStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private BookingClaim()
    {
    }

    public static BookingClaim Requested(long bookingId) => Create(bookingId, BookingClaimStatus.Requested);

    public static BookingClaim Cancelled(long bookingId) => Create(bookingId, BookingClaimStatus.Cancelled);

    private static BookingClaim Create(long bookingId, BookingClaimStatus status)
    {
        if (bookingId <= 0)
            throw new PaymentDomainException("A claim needs the booking it claims.");

        return new BookingClaim { BookingId = bookingId, Status = status };
    }
}
