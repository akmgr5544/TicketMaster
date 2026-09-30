using Bookings.Application.Commands;
using Bookings.Application.Commands.Bookings;
using Bookings.Application.Commands.Payments;
using Bookings.Application.Exceptions;
using Bookings.Application.Queries;
using Bookings.Application.Services;
using Bookings.Domain.Enums;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.Common.IntegrationEvents;

namespace BookingIntegration.Handlers;

// What Bookings tells the payment service, observed through the recording publisher. Every message must be
// staged inside the transaction that wrote the state it describes: that is what lets the real outbox drop
// it on rollback, which BookingsHostFixture's relay tests prove against a live broker.
public sealed class IntegrationEventPublishingTests : IntegrationTest
{
    private const string EventId = "evt-1";

    public IntegrationEventPublishingTests(BookingsFixture fixture) : base(fixture)
    {
    }

    private IntegrationEventLog Log => Act.GetRequiredService<IntegrationEventLog>();

    // --- PaymentRequested ---

    [Fact]
    public async Task Making_a_booking_requests_payment_for_it()
    {
        var ids = await ReservedTicketsAsync("A1", "A2");

        var bookingId = await Sender.Send(new MakeBookingCommand(TestUsers.Owner, EventId, ids));

        var published = Assert.Single(Log.Published);
        var request = Assert.IsType<PaymentRequestedIntegrationEvent>(published.Event);
        Assert.Equal(bookingId, request.BookingId);
        Assert.Equal(TestUsers.Owner, request.BuyerId);
        Assert.Equal(PaymentPricing.SellerFor(EventId), request.SellerId);
        Assert.Equal(100m, request.Amount);
        Assert.Equal("USD", request.Currency);
        Assert.NotNull(published.TransactionId);
    }

    [Fact]
    public void The_placeholder_seller_is_stable_per_event_and_is_not_the_buyer()
    {
        Assert.Equal(PaymentPricing.SellerFor(EventId), PaymentPricing.SellerFor(EventId));
        Assert.NotEqual(PaymentPricing.SellerFor(EventId), PaymentPricing.SellerFor("evt-2"));
        Assert.NotEqual(Guid.Empty, PaymentPricing.SellerFor(EventId));
        Assert.NotEqual(TestUsers.Owner, PaymentPricing.SellerFor(EventId));
    }

    [Fact]
    public async Task A_refused_booking_requests_no_payment()
    {
        var tickets = await Seed.TicketsAsync(EventId, "A1");

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new MakeBookingCommand(TestUsers.Owner, EventId, [tickets[0].Id])));

        Assert.Empty(Log.Published);
    }

    [Fact]
    public async Task A_rolled_back_booking_staged_its_payment_request_inside_the_rolled_back_transaction()
    {
        var ids = await ReservedTicketsAsync("A1");
        Act.GetRequiredService<AfterHandlerFailureSwitch>().ShouldFailAfterHandler = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sender.Send(new MakeBookingCommand(TestUsers.Owner, EventId, ids)));

        Assert.Equal(0, await ReadAsync(context => context.Bookings.CountAsync()));
        var published = Assert.Single(Log.Published);
        Assert.IsType<PaymentRequestedIntegrationEvent>(published.Event);
        Assert.NotNull(published.TransactionId);
    }

    // --- BookingCancelled ---

    [Fact]
    public async Task Cancelling_a_booking_announces_it()
    {
        var booking = await BookedAsync();

        await Sender.Send(new CancelBookingCommand(booking, TestUsers.Owner));

        AssertCancelledOnce(booking);
    }

    [Fact]
    public async Task A_failed_payment_announces_the_cancellation()
    {
        var booking = await BookedAsync();

        await Sender.Send(new ReleaseUnpaidBookingCommand(booking));

        AssertCancelledOnce(booking);
    }

    [Fact]
    public async Task A_relocation_that_cancels_the_booking_announces_it()
    {
        var tickets = await Seed.TicketsAsync(EventId, Seed.Soon, eventVersion: 1, "A1", "A2");
        var booking = await Seed.BookingAsync(TestUsers.Owner, tickets[0].Id);

        await Sender.Send(new ReconcileEventVenueCommand(EventId, 2, "venue-2", Seed.Soon, ["A2"]));

        AssertCancelledOnce(booking.Id);
    }

    [Fact]
    public async Task A_relocation_that_flags_a_paid_booking_for_refund_announces_no_cancellation()
    {
        var tickets = await Seed.TicketsAsync(EventId, Seed.Soon, eventVersion: 1, "A1", "A2");
        var booking = await Seed.BookingAsync(TestUsers.Owner, tickets[0].Id);
        await Sender.Send(new ConfirmBookingCommand(booking.Id));

        await Sender.Send(new ReconcileEventVenueCommand(EventId, 2, "venue-2", Seed.Soon, ["A2"]));

        Assert.Empty(Log.OfType<BookingCancelledIntegrationEvent>());
    }

    [Fact]
    public async Task Cancelling_twice_announces_it_once()
    {
        var booking = await BookedAsync();

        await Sender.Send(new CancelBookingCommand(booking, TestUsers.Owner));
        await Sender.Send(new ReleaseUnpaidBookingCommand(booking));

        AssertCancelledOnce(booking);
    }

    [Fact]
    public async Task A_rolled_back_cancellation_staged_its_announcement_inside_the_rolled_back_transaction()
    {
        var booking = await BookedAsync();
        Act.GetRequiredService<AfterHandlerFailureSwitch>().ShouldFailAfterHandler = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Sender.Send(new CancelBookingCommand(booking, TestUsers.Owner)));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking));
        Assert.Equal(BookingStatus.Booked, stored.Status);
        var published = Assert.Single(Log.Published);
        Assert.IsType<BookingCancelledIntegrationEvent>(published.Event);
        Assert.NotNull(published.TransactionId);
    }

    private void AssertCancelledOnce(long bookingId)
    {
        var published = Assert.Single(Log.Published);
        var cancelled = Assert.IsType<BookingCancelledIntegrationEvent>(published.Event);
        Assert.Equal(bookingId, cancelled.BookingId);
        Assert.NotNull(published.TransactionId);
    }

    private async Task<long[]> ReservedTicketsAsync(params string[] seats)
    {
        var ids = (await Seed.TicketsAsync(EventId, seats)).Select(t => t.Id).ToArray();
        await Seed.ReservationAsync(TestUsers.Owner, EventId, ids);
        return ids;
    }

    private async Task<long> BookedAsync()
    {
        var tickets = await Seed.TicketsAsync(EventId, "A1");
        return (await Seed.BookingAsync(TestUsers.Owner, tickets[0].Id)).Id;
    }
}
