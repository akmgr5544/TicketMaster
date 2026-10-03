using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain;
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

    // The cancellation overtook PaymentRequested, or the booking never reached payment. Either way it is kept,
    // so a PaymentRequested that arrives later opens nothing to pay.
    [Fact]
    public async Task Cancelling_a_booking_with_no_checkout_records_the_cancellation_and_touches_nothing_else()
    {
        var unrelated = await SeedCheckoutAsync(OrderState.Executing);
        var bookingId = unrelated.BookingId == long.MaxValue ? 1 : unrelated.BookingId + 1;

        await CancelAsync(bookingId);

        Assert.Empty(Outbox.Published);
        Assert.Equal(PaymentOrderStatus.Executing, (await ReadOrderAsync(unrelated.OrderId(0))).Status);
        Assert.Equal(1, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        Assert.Equal([bookingId], await ReadAsync(c => c.BookingClaims
            .Where(b => b.Status == BookingClaimStatus.Cancelled)
            .Select(b => b.BookingId)
            .ToListAsync()));
    }

    // The request's claim and checkout are uncommitted, so the cancellation's check sees no checkout; its own
    // claim insert must wait on the key, then cancel the checkout it lost to rather than record a cancellation.
    [Fact]
    public async Task A_cancellation_racing_a_request_waits_on_the_claim_and_cancels_the_checkout()
    {
        var checkout = CheckoutSeed.Create(Guid.CreateVersion7(), 5150, Guid.NewGuid(), CheckoutSeed.Lines(1));
        checkout.ClearDomainEvents();
        await using var requestScope = NewScope();
        var requestContext = requestScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await using var requestTransaction = await requestContext.Database.BeginTransactionAsync();
        requestContext.BookingClaims.Add(BookingClaim.Requested(checkout.BookingId));
        requestContext.PaymentEvents.Add(checkout);
        await requestContext.SaveChangesAsync();

        var cancel = CancelAsync(checkout.BookingId);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(cancel.IsCompleted, "The cancellation should wait on the request's uncommitted claim.");
        await requestTransaction.CommitAsync();
        await cancel;

        Assert.Equal(PaymentOrderStatus.Failed, (await ReadOrderAsync(checkout.PaymentOrders.Single().PaymentOrderId)).Status);
        Assert.Equal(checkout.BookingId, Assert.Single(Outbox.OfType<BookingPaymentFailedIntegrationEvent>()).BookingId);
        Assert.Equal(0, await ReadAsync(c => c.BookingClaims.CountAsync(b => b.Status == BookingClaimStatus.Cancelled)));
    }

    // A checkout stored before claims existed has none; it is still found and cancelled, never claimed over.
    [Fact]
    public async Task Cancelling_a_checkout_that_predates_claims_cancels_it()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        await InScopeAsync(async c =>
        {
            await c.BookingClaims.Where(b => b.BookingId == checkout.BookingId).ExecuteDeleteAsync();
        });

        await CancelAsync(checkout.BookingId);

        Assert.Equal(PaymentOrderStatus.Failed, (await ReadOrderAsync(checkout.OrderId(0))).Status);
        Assert.Equal(0, await ReadAsync(c => c.BookingClaims.CountAsync()));
    }

    [Fact]
    public async Task A_redelivered_cancellation_with_no_checkout_is_recorded_once()
    {
        await CancelAsync(77);

        await CancelAsync(77);

        Assert.Equal(1, await ReadAsync(c => c.BookingClaims.CountAsync(b => b.Status == BookingClaimStatus.Cancelled)));
    }

    // A checkout exists, so its failed orders already say the booking is cancelled; nothing more to remember.
    [Fact]
    public async Task Cancelling_a_booking_that_has_a_checkout_records_no_separate_cancellation()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        await CancelAsync(checkout.BookingId);

        Assert.Equal(0, await ReadAsync(c => c.BookingClaims.CountAsync(b => b.Status == BookingClaimStatus.Cancelled)));
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
