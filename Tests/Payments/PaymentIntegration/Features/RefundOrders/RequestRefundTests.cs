using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.RefundOrders;

// Refunds are whole only: a request gives back everything still paid on the checkout's one order.
public sealed class RequestRefundTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> RequestAsync(RefundRequestedIntegrationEvent message) =>
        HandleAsync<RefundRequestedHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(message, context, CancellationToken.None));

    private Task<List<RefundOrder>> ReadRefundsAsync() => ReadAsync(c => c.RefundOrders.ToListAsync());

    [Fact]
    public async Task A_refund_request_refunds_everything_paid_under_bookings_refund_id()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 100m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        var refundId = Guid.NewGuid();

        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId, refundId));

        var refund = Assert.Single(await ReadRefundsAsync());
        Assert.Equal(orderId, refund.PaymentOrderId);
        Assert.Equal(refundId, refund.RequestId);
        Assert.Equal(100m, refund.RefundAmount.Amount);
        Assert.Equal(Usd, refund.RefundAmount.Currency);
        Assert.Equal(RefundOrderStatus.Pending, refund.Status);
        Assert.Equal(refund.RefundOrderId, Assert.Single(outgoing.OfType<ProcessRefund>()).RefundOrderId);
    }

    [Fact]
    public async Task A_failed_refund_does_not_count_against_what_is_left()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 50m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        var failed = await SeedRefundAsync(orderId, 50m);
        await InScopeAsync(async c =>
        {
            (await c.RefundOrders.SingleAsync(r => r.RefundOrderId == failed.RefundOrderId)).MarkAsFailed();
            await c.SaveChangesAsync();
        });
        var refundId = Guid.NewGuid();

        await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId, refundId));

        Assert.Equal(50m, (await ReadRefundsAsync()).Single(r => r.RequestId == refundId).RefundAmount.Amount);
    }

    [Fact]
    public async Task A_refund_request_without_a_refund_id_uses_the_orders_own_id()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 50m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId));

        Assert.Equal(orderId, Assert.Single(await ReadRefundsAsync()).RequestId);
    }

    [Fact]
    public async Task A_redelivered_refund_request_records_nothing_new()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 50m);
        var message = new RefundRequestedIntegrationEvent(checkout.BookingId, Guid.NewGuid());
        await RequestAsync(message);

        var outgoing = await RequestAsync(message);

        Assert.Empty(outgoing);
        Assert.Single(await ReadRefundsAsync());
    }

    [Theory]
    [InlineData(OrderState.Init)]
    [InlineData(OrderState.Executing)]
    [InlineData(OrderState.Failed)]
    public async Task A_refund_request_for_a_booking_with_nothing_paid_records_nothing(OrderState state)
    {
        var checkout = await SeedCheckoutAsync(state, amount: 100m);

        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId, Guid.NewGuid()));

        Assert.Empty(outgoing);
        Assert.Empty(await ReadRefundsAsync());
    }

    [Fact]
    public async Task A_refund_request_for_a_booking_with_no_checkout_records_nothing()
    {
        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid()));

        Assert.Empty(outgoing);
        Assert.Empty(await ReadRefundsAsync());
    }
}
