namespace Notifications.Domain;

// Who bought a booking. Booking outcome messages carry only the booking id; PaymentRequested is the one message that
// names the buyer, so it is remembered here.
public class BookingBuyer
{
    public long BookingId { get; private set; }
    public Guid BuyerId { get; private set; }

    private BookingBuyer() { }

    public BookingBuyer(long bookingId, Guid buyerId)
    {
        BookingId = bookingId;
        BuyerId = buyerId;
    }
}
