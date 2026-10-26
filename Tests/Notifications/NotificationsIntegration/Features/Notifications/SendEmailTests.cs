using Microsoft.EntityFrameworkCore;
using Notifications.Domain;
using Notifications.Email;
using Notifications.Features.Notifications;
using NotificationsIntegration.Fixtures;

namespace NotificationsIntegration.Features.Notifications;

public sealed class SendEmailTests(NotificationsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task A_queued_email_goes_to_the_buyer_and_is_marked_sent()
    {
        var notification = new Notification(NewBookingId(), NotificationKind.BookingPaid, null, "buyer@example.com", DateTime.UtcNow);
        await SeedAsync(notification);

        await HandleAsync<SendEmailHandler>((handler, context) => handler.Handle(new SendEmail(notification.NotificationId), context, default));

        var sent = Assert.Single(Emails.Sent);
        Assert.Equal("buyer@example.com", sent.To);
        Assert.Equal(notification.Subject, sent.Subject);
        Assert.NotNull((await ReadAsync(c => c.Notifications.SingleAsync(n => n.NotificationId == notification.NotificationId))).SentAt);
    }

    [Fact]
    public async Task An_email_already_sent_is_not_sent_again()
    {
        var notification = new Notification(NewBookingId(), NotificationKind.BookingPaid, null, "buyer@example.com", DateTime.UtcNow);
        await SeedAsync(notification);

        await HandleAsync<SendEmailHandler>((handler, context) => handler.Handle(new SendEmail(notification.NotificationId), context, default));
        await HandleAsync<SendEmailHandler>((handler, context) => handler.Handle(new SendEmail(notification.NotificationId), context, default));

        Assert.Single(Emails.Sent);
    }

    // The failure propagates, so the message's retry policy sends it again later; it is not marked sent.
    [Fact]
    public async Task A_mail_server_that_is_down_fails_the_send_so_it_is_retried()
    {
        var notification = new Notification(NewBookingId(), NotificationKind.BookingPaid, null, "buyer@example.com", DateTime.UtcNow);
        await SeedAsync(notification);
        Emails.Fail = true;

        await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            HandleAsync<SendEmailHandler>((handler, context) => handler.Handle(new SendEmail(notification.NotificationId), context, default)));

        Assert.Null((await ReadAsync(c => c.Notifications.SingleAsync(n => n.NotificationId == notification.NotificationId))).SentAt);
    }

    [Fact]
    public async Task A_refund_email_names_the_refund()
    {
        var refundId = Guid.NewGuid();
        var notification = new Notification(NewBookingId(), NotificationKind.BookingRefunded, refundId, "buyer@example.com", DateTime.UtcNow);
        await SeedAsync(notification);

        await HandleAsync<SendEmailHandler>((handler, context) => handler.Handle(new SendEmail(notification.NotificationId), context, default));

        Assert.Contains(refundId.ToString(), Assert.Single(Emails.Sent).Body);
    }
}
