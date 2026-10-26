using Microsoft.EntityFrameworkCore;
using Notifications.Data;
using Notifications.Email;
using Wolverine.Attributes;

namespace Notifications.Features.Notifications;

// Pinned so a rename cannot strand the envelopes already stored under the old name.
[MessageIdentity("send-email")]
public sealed record SendEmail(Guid NotificationId);

// Non-transactional: the mail server is called with no database transaction open, and a failure to reach it throws
// EmailDeliveryException, which the retry policy schedules again. Marking the notification sent afterwards is a single
// save. A crash between the two sends the email again on redelivery — at least once, never lost.
[NonTransactional]
public sealed class SendEmailHandler(IEmailSender sender, TimeProvider clock)
{
    public async Task Handle(SendEmail message, NotificationsDbContext context, CancellationToken cancellationToken)
    {
        var notification = await context.Notifications
            .SingleOrDefaultAsync(n => n.NotificationId == message.NotificationId, cancellationToken);

        if (notification is null || notification.SentAt is not null)
            return;

        await sender.SendAsync(notification.Recipient, notification.Subject, notification.Body, cancellationToken);

        notification.MarkSent(clock.GetUtcNow().UtcDateTime);
        await context.SaveChangesAsync(cancellationToken);
    }
}
