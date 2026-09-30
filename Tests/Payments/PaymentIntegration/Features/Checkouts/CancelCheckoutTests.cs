using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentIntegration.Features.Checkouts;

public sealed class CancelCheckoutTests : MessagingTest
{
    private readonly LogCapture _logs;

    public CancelCheckoutTests(PaymentsFixture fixture) : base(fixture)
    {
        _logs = fixture.Services.GetRequiredService<LogCapture>();
        _logs.Clear();
    }

    private async Task CancelAsync(long bookingId)
    {
        await using var scope = NewScope();
        await new BookingCancelledConsumer(scope.ServiceProvider.GetRequiredService<ISender>())
            .Consume(new BookingCancelledIntegrationEvent(bookingId), CancellationToken.None);
    }

    [Theory]
    [InlineData(OrderState.NotStarted)]
    [InlineData(OrderState.Executing)]
    public async Task Cancelling_fails_the_unpaid_order_and_tells_Bookings(OrderState state)
    {
        var checkout = await SeedCheckoutAsync(state);

        await CancelAsync(checkout.BookingId);

        Assert.Equal(PaymentOrderStatus.Failed, (await ReadOrderAsync(checkout.OrderId(0))).Status);
        var published = Assert.Single(Outbox.Published);
        Assert.Equal(new BookingPaymentFailedIntegrationEvent(checkout.BookingId), published.Event);
        Assert.NotNull(published.TransactionId);
    }

    [Fact]
    public async Task Cancelling_a_paid_booking_reverses_nothing_and_flags_it_for_a_refund()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var before = await ReadCheckoutAsync(checkout.CheckoutId);

        await CancelAsync(checkout.BookingId);

        var after = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Success, after.Order(checkout.OrderId(0)).Status);
        Assert.True(after.IsPaymentDone);
        Assert.Equal(before.Version, after.Version);
        Assert.Empty(Outbox.Published);
        Assert.Contains(_logs.Lines, line => line.StartsWith("Warning")
                                             && line.Contains(checkout.BookingId.ToString())
                                             && line.Contains("refund"));
    }

    [Fact]
    public async Task Cancelling_a_partly_paid_booking_fails_the_rest_and_flags_the_paid_order()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, OrderState.Executing);

        await CancelAsync(checkout.BookingId);

        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Success, stored.Order(checkout.OrderId(0)).Status);
        Assert.Equal(PaymentOrderStatus.Failed, stored.Order(checkout.OrderId(1)).Status);
        Assert.Equal(checkout.BookingId, Assert.Single(Outbox.OfType<BookingPaymentFailedIntegrationEvent>()).BookingId);
        Assert.Contains(_logs.Lines, line => line.StartsWith("Warning") && line.Contains("refund"));
    }

    // The cancellation overtook PaymentRequested, or the booking never reached payment.
    [Fact]
    public async Task Cancelling_a_booking_with_no_checkout_is_a_no_op()
    {
        var unrelated = await SeedCheckoutAsync(OrderState.Executing);

        await CancelAsync(unrelated.BookingId == long.MaxValue ? 1 : unrelated.BookingId + 1);

        Assert.Empty(Outbox.Published);
        Assert.Equal(PaymentOrderStatus.Executing, (await ReadOrderAsync(unrelated.OrderId(0))).Status);
        Assert.Equal(1, await ReadAsync(c => c.PaymentEvents.CountAsync()));
    }

    [Fact]
    public async Task A_redelivered_cancellation_changes_nothing_and_publishes_nothing_more()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        await CancelAsync(checkout.BookingId);
        var version = (await ReadCheckoutAsync(checkout.CheckoutId)).Version;

        await CancelAsync(checkout.BookingId);

        Assert.Single(Outbox.Published);
        Assert.Equal(version, (await ReadCheckoutAsync(checkout.CheckoutId)).Version);
        Assert.Equal(PaymentOrderStatus.Failed, (await ReadOrderAsync(checkout.OrderId(0))).Status);
    }

    [Fact]
    public async Task The_expiry_firing_after_a_cancellation_is_a_no_op()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        await CancelAsync(checkout.BookingId);

        var result = await SendAsync(new ExpireCheckout.Command(checkout.CheckoutId));

        Assert.Equal(0, result.Value!.OrdersFailed);
        Assert.Single(Outbox.Published);
    }
}
