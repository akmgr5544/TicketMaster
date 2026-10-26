using Microsoft.EntityFrameworkCore;
using Notifications.Data;
using Notifications.Domain;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace Notifications.Features.Notifications;

// Tells a booking's buyer what happened to it: one email per outcome. The notification is recorded here, in the
// message's transaction, and SendEmail goes out through the outbox once it commits — the mail server is never called
// inside a transaction.
public sealed class BookingOutcomeHandler(TimeProvider clock, ILogger<BookingOutcomeHandler> logger)
{
    public Task<OutgoingMessages> Handle(BookingPaidIntegrationEvent message, NotificationsDbContext context,
        CancellationToken cancellationToken) =>
        NotifyAsync(context, message.BookingId, NotificationKind.BookingPaid, null, cancellationToken);

    public Task<OutgoingMessages> Handle(BookingPaymentFailedIntegrationEvent message, NotificationsDbContext context,
        CancellationToken cancellationToken) =>
        NotifyAsync(context, message.BookingId, NotificationKind.PaymentFailed, null, cancellationToken);

    public Task<OutgoingMessages> Handle(BookingCancelledIntegrationEvent message, NotificationsDbContext context,
        CancellationToken cancellationToken) =>
        NotifyAsync(context, message.BookingId, NotificationKind.BookingCancelled, null, cancellationToken);

    public Task<OutgoingMessages> Handle(BookingRefundedIntegrationEvent message, NotificationsDbContext context,
        CancellationToken cancellationToken) =>
        NotifyAsync(context, message.BookingId, NotificationKind.BookingRefunded, message.RefundId, cancellationToken);

    private async Task<OutgoingMessages> NotifyAsync(NotificationsDbContext context, long bookingId, NotificationKind kind,
        Guid? refundId, CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();
        var refundKey = refundId ?? Guid.Empty;

        // A redelivery: this outcome was already recorded, and its email already queued.
        if (await context.Notifications.AnyAsync(
                n => n.BookingId == bookingId && n.Kind == kind && n.RefundId == refundKey, cancellationToken))
            return outgoing;

        // A voided booking produces both: Bookings announces the cancellation, and Payments fails its payment. The
        // buyer is told once, by whichever arrives first.
        if (kind is NotificationKind.PaymentFailed or NotificationKind.BookingCancelled
            && await context.Notifications.AnyAsync(
                n => n.BookingId == bookingId
                     && (n.Kind == NotificationKind.PaymentFailed || n.Kind == NotificationKind.BookingCancelled),
                cancellationToken))
            return outgoing;

        // Logged and dropped rather than retried: a booking or an address this service never heard of will not
        // appear by asking again.
        var buyer = await context.BookingBuyers.SingleOrDefaultAsync(b => b.BookingId == bookingId, cancellationToken);
        if (buyer is null)
        {
            logger.LogWarning("No buyer is known for booking {BookingId}; its {Kind} notification is skipped.", bookingId, kind);
            return outgoing;
        }

        var recipient = await context.Recipients.SingleOrDefaultAsync(r => r.UserId == buyer.BuyerId, cancellationToken);
        if (recipient is null)
        {
            logger.LogWarning("No address is known for user {UserId}; the {Kind} notification for booking {BookingId} is skipped.",
                buyer.BuyerId, kind, bookingId);
            return outgoing;
        }

        var notification = new Notification(bookingId, kind, refundId, recipient.Email, clock.GetUtcNow().UtcDateTime);
        context.Notifications.Add(notification);
        outgoing.Add(new SendEmail(notification.NotificationId));
        return outgoing;
    }
}
