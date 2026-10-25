using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.Checkouts;

public sealed class RequestPaymentTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> HandleAsync(PaymentRequestedIntegrationEvent message) =>
        HandleAsync<PaymentRequestedHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(message, context, CancellationToken.None));

    [Fact]
    public async Task A_payment_request_opens_one_checkout_with_one_order_for_the_seller()
    {
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();
        var bookingId = NewBookingId();

        await HandleAsync(new PaymentRequestedIntegrationEvent(bookingId, buyer, seller, 120.50m, "usd"));

        var checkout = await ReadCheckoutAsync(bookingId);
        Assert.Equal(buyer, checkout.BuyerId);
        Assert.False(checkout.IsPaymentDone);
        Assert.Equal(120.50m, checkout.TotalAmount.Amount);
        Assert.Equal("USD", checkout.TotalAmount.Currency);

        var order = Assert.Single(checkout.PaymentOrders);
        Assert.Equal(seller, order.SellerId);
        Assert.Equal(120.50m, order.Amount.Amount);
        Assert.Equal("USD", order.Amount.Currency);
        Assert.Equal(PaymentOrderStatus.Init, order.Status);
    }

    [Fact]
    public async Task A_redelivered_request_creates_nothing_more_and_sends_nothing()
    {
        var message = new PaymentRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid(), Guid.NewGuid(), 40m, "USD");
        await HandleAsync(message);

        var outgoing = await HandleAsync(message);

        Assert.Empty(outgoing);
        var count = await ReadAsync(c => c.PaymentEvents.CountAsync(e => e.BookingId == message.BookingId));
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData(0, "USD")]
    [InlineData(-5, "USD")]
    [InlineData(1.005, "USD")]
    [InlineData(10, "US1")]
    [InlineData(10, "USDD")]
    public async Task A_request_the_domain_refuses_opens_nothing_and_fails_the_payment(double amount, string currency)
    {
        var bookingId = NewBookingId();

        var outgoing = await HandleAsync(
            new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), (decimal)amount, currency));

        var failed = Assert.Single(outgoing.OfType<BookingPaymentFailedIntegrationEvent>());
        Assert.Equal(bookingId, failed.BookingId);
        Assert.False(await ReadAsync(c => c.PaymentEvents.AnyAsync(e => e.BookingId == bookingId)));
    }

    [Fact]
    public async Task An_accepted_request_claims_the_booking_together_with_the_checkout()
    {
        var bookingId = NewBookingId();

        await HandleAsync(new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), 40m, "USD"));

        var claim = await ReadAsync(c => c.BookingClaims.SingleAsync(x => x.BookingId == bookingId));
        Assert.Equal(BookingClaimStatus.Requested, claim.Status);
        Assert.True(await ReadAsync(c => c.PaymentEvents.AnyAsync(e => e.BookingId == bookingId)));
    }

    [Fact]
    public async Task A_refused_request_claims_nothing()
    {
        var bookingId = NewBookingId();

        await HandleAsync(new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), 0m, "USD"));

        Assert.False(await ReadAsync(c => c.BookingClaims.AnyAsync(x => x.BookingId == bookingId)));
    }

    // Bookings released the seats when it cancelled, so a BookingPaymentFailed would only be noise.
    [Fact]
    public async Task A_request_for_a_booking_already_cancelled_opens_nothing_and_sends_nothing()
    {
        var bookingId = NewBookingId();
        await SeedClaimAsync(BookingClaim.Cancelled(bookingId));

        var outgoing = await HandleAsync(
            new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), 40m, "USD"));

        Assert.Empty(outgoing);
        Assert.False(await ReadAsync(c => c.PaymentEvents.AnyAsync(e => e.BookingId == bookingId)));
        var claim = await ReadAsync(c => c.BookingClaims.SingleAsync(x => x.BookingId == bookingId));
        Assert.Equal(BookingClaimStatus.Cancelled, claim.Status);
    }

    [Fact]
    public async Task An_accepted_request_schedules_the_checkout_to_expire_in_fifteen_minutes()
    {
        var bookingId = NewBookingId();

        var outgoing = await HandleAsync(
            new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), 40m, "USD"));

        var expiry = Assert.Single(outgoing.OfType<DeliveryMessage<CheckoutExpiryDue>>());
        Assert.Equal(bookingId, expiry.Message.BookingId);
        Assert.Equal(TimeSpan.FromMinutes(15), expiry.Options.ScheduleDelay);
    }
}
