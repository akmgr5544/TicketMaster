namespace Notifications.Domain;

// One email to a buyer about one outcome of one booking. (BookingId, Kind, RefundId) is unique, so a redelivered
// outcome finds its notification already recorded and sends nothing. RefundId is Guid.Empty for anything but a
// refund, which keeps the key free of nulls.
public class Notification
{
    public Guid NotificationId { get; private set; }
    public long BookingId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public Guid RefundId { get; private set; }
    public string Recipient { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    // Set once the email is handed to the SMTP server; a redelivered send finds it set and sends nothing.
    public DateTime? SentAt { get; private set; }

    private Notification() { }

    public Notification(long bookingId, NotificationKind kind, Guid? refundId, string recipient, DateTime createdAt)
    {
        NotificationId = Guid.CreateVersion7();
        BookingId = bookingId;
        Kind = kind;
        RefundId = refundId ?? Guid.Empty;
        Recipient = recipient;
        CreatedAt = createdAt;
    }

    public string Subject => Kind switch
    {
        NotificationKind.BookingPaid => $"Booking {BookingId} is confirmed",
        NotificationKind.PaymentFailed => $"Payment for booking {BookingId} did not go through",
        NotificationKind.BookingCancelled => $"Booking {BookingId} was cancelled",
        _ => $"Your refund for booking {BookingId} is on its way"
    };

    public string Body => Kind switch
    {
        NotificationKind.BookingPaid => $"We received your payment. Booking {BookingId} is confirmed.",
        NotificationKind.PaymentFailed =>
            $"The payment for booking {BookingId} failed or was not completed in time, so the booking was voided and its seats released.",
        NotificationKind.BookingCancelled => $"Booking {BookingId} was cancelled.",
        _ => RefundId == Guid.Empty
            ? $"The money for booking {BookingId} is being returned to your payment method."
            : $"Refund {RefundId} for booking {BookingId} is being returned to your payment method."
    };

    public void MarkSent(DateTime sentAt) => SentAt = sentAt;
}
