using Microsoft.EntityFrameworkCore;
using Notifications.Domain;
using Notifications.Features.Notifications;
using NotificationsIntegration.Fixtures;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace NotificationsIntegration.Features.Notifications;

// One email per outcome per booking, to the booking's buyer — never a second one for a redelivery, and one for a
// voided booking, however many messages announce it.
public sealed class NotifyBuyerTests(NotificationsFixture fixture) : IntegrationTest(fixture)
{
    public static TheoryData<string> Outcomes => ["paid", "failed", "cancelled", "refunded", "refund-failed"];

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Each_outcome_records_an_email_to_the_buyer_and_queues_it(string outcome)
    {
        var (bookingId, email) = await SeedKnownBuyerAsync();

        var outgoing = await HandleOutcomeAsync(outcome, bookingId);

        var notification = await ReadAsync(c => c.Notifications.SingleAsync(n => n.BookingId == bookingId));
        Assert.Equal(email, notification.Recipient);
        Assert.Equal(KindOf(outcome), notification.Kind);
        var send = Assert.Single(outgoing.OfType<SendEmail>());
        Assert.Equal(notification.NotificationId, send.NotificationId);
    }

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task A_redelivered_outcome_queues_no_second_email(string outcome)
    {
        var (bookingId, _) = await SeedKnownBuyerAsync();
        await HandleOutcomeAsync(outcome, bookingId);

        var again = await HandleOutcomeAsync(outcome, bookingId);

        Assert.Empty(again);
        Assert.Equal(1, await ReadAsync(c => c.Notifications.CountAsync(n => n.BookingId == bookingId)));
    }

    [Theory]
    [InlineData("cancelled", "failed")]
    [InlineData("failed", "cancelled")]
    public async Task A_voided_booking_is_announced_once_whichever_message_arrives_first(string first, string second)
    {
        var (bookingId, _) = await SeedKnownBuyerAsync();
        await HandleOutcomeAsync(first, bookingId);

        var outgoing = await HandleOutcomeAsync(second, bookingId);

        Assert.Empty(outgoing);
        var notification = await ReadAsync(c => c.Notifications.SingleAsync(n => n.BookingId == bookingId));
        Assert.Equal(KindOf(first), notification.Kind);
    }

    // Both messages handled at the same instant both pass the handler's read-first check; the database is what
    // refuses the second, and the retry then finds the first.
    [Fact]
    public async Task The_database_refuses_a_second_voided_email_for_one_booking()
    {
        var (bookingId, email) = await SeedKnownBuyerAsync();
        await SeedAsync(new Notification(bookingId, NotificationKind.PaymentFailed, null, email, DateTime.UtcNow));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SeedAsync(new Notification(bookingId, NotificationKind.BookingCancelled, null, email, DateTime.UtcNow)));
    }

    [Fact]
    public async Task Each_refund_of_a_booking_gets_its_own_email()
    {
        var (bookingId, _) = await SeedKnownBuyerAsync();
        Guid[] refunds = [Guid.NewGuid(), Guid.NewGuid()];

        foreach (var refundId in refunds)
            await HandleAsync<BookingOutcomeHandler, OutgoingMessages>((handler, context) =>
                handler.Handle(new BookingRefundedIntegrationEvent(bookingId, refundId), context, default));

        var recorded = await ReadAsync(c => c.Notifications.Where(n => n.BookingId == bookingId).Select(n => n.RefundId).ToListAsync());
        Assert.Equal(refunds.Order(), recorded.Order());
    }

    [Fact]
    public async Task An_outcome_for_a_booking_whose_buyer_is_unknown_is_skipped()
    {
        var bookingId = NewBookingId();

        var outgoing = await HandleOutcomeAsync("paid", bookingId);

        Assert.Empty(outgoing);
        Assert.False(await ReadAsync(c => c.Notifications.AnyAsync(n => n.BookingId == bookingId)));
    }

    [Fact]
    public async Task An_outcome_for_a_buyer_with_no_address_is_skipped()
    {
        var bookingId = NewBookingId();
        await SeedAsync(new BookingBuyer(bookingId, Guid.NewGuid()));

        var outgoing = await HandleOutcomeAsync("paid", bookingId);

        Assert.Empty(outgoing);
        Assert.False(await ReadAsync(c => c.Notifications.AnyAsync(n => n.BookingId == bookingId)));
    }

    private Task<OutgoingMessages> HandleOutcomeAsync(string outcome, long bookingId) =>
        HandleAsync<BookingOutcomeHandler, OutgoingMessages>((handler, context) => outcome switch
        {
            "paid" => handler.Handle(new BookingPaidIntegrationEvent(bookingId), context, default),
            "failed" => handler.Handle(new BookingPaymentFailedIntegrationEvent(bookingId), context, default),
            "cancelled" => handler.Handle(new BookingCancelledIntegrationEvent(bookingId), context, default),
            "refund-failed" => handler.Handle(new BookingRefundFailedIntegrationEvent(bookingId, RefundIdFor(bookingId)), context, default),
            _ => handler.Handle(new BookingRefundedIntegrationEvent(bookingId, RefundIdFor(bookingId)), context, default)
        });

    // The same refund every time for one booking, so a second call is a redelivery rather than another refund.
    private static Guid RefundIdFor(long bookingId) => new(BitConverter.GetBytes(bookingId).Concat(new byte[8]).ToArray());

    private static NotificationKind KindOf(string outcome) => outcome switch
    {
        "paid" => NotificationKind.BookingPaid,
        "failed" => NotificationKind.PaymentFailed,
        "cancelled" => NotificationKind.BookingCancelled,
        "refund-failed" => NotificationKind.RefundFailed,
        _ => NotificationKind.BookingRefunded
    };
}
