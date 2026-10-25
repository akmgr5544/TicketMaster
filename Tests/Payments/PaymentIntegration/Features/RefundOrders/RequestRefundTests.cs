using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.RefundOrders;

public sealed class RequestRefundTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> RequestAsync(RefundRequestedIntegrationEvent message) =>
        HandleAsync<RefundRequestedHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(message, context, CancellationToken.None));

    private Task<List<RefundOrder>> ReadRefundsAsync() => ReadAsync(c => c.RefundOrders.ToListAsync());

    [Fact]
    public async Task A_whole_booking_refund_refunds_each_paid_order_for_what_is_left_under_bookings_refund_id()
    {
        var checkout = await SeedCheckoutWithOrdersAsync(Guid.NewGuid(), NewBookingId(),
            (OrderState.Success, 40m), (OrderState.Success, 60m), (OrderState.Failed, 25m));
        var first = checkout.PaymentOrders.ElementAt(0).PaymentOrderId;
        var second = checkout.PaymentOrders.ElementAt(1).PaymentOrderId;
        await SeedSettledRefundAsync(second, 15m);
        var refundId = Guid.NewGuid();

        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId, refundId));

        var refunds = (await ReadRefundsAsync()).Where(r => r.RequestId == refundId).ToList();
        Assert.Equal(2, refunds.Count);
        Assert.Equal(40m, refunds.Single(r => r.PaymentOrderId == first).RefundAmount.Amount);
        Assert.Equal(45m, refunds.Single(r => r.PaymentOrderId == second).RefundAmount.Amount);
        Assert.All(refunds, r => Assert.Equal(RefundOrderStatus.Pending, r.Status));
        Assert.Equal(
            refunds.Select(r => r.RefundOrderId).OrderBy(id => id),
            outgoing.OfType<ProcessRefund>().Select(p => p.RefundOrderId).OrderBy(id => id));
    }

    [Fact]
    public async Task A_failed_refund_does_not_count_against_what_is_left()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 50m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        var failed = await SeedRefundAsync(orderId, 20m);
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
    public async Task A_whole_booking_refund_without_a_refund_id_uses_the_orders_own_id()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 50m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId));

        Assert.Equal(orderId, Assert.Single(await ReadRefundsAsync()).RequestId);
    }

    [Fact]
    public async Task A_redelivered_whole_booking_refund_records_nothing_new()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 50m);
        var message = new RefundRequestedIntegrationEvent(checkout.BookingId, Guid.NewGuid());
        await RequestAsync(message);

        var outgoing = await RequestAsync(message);

        Assert.Empty(outgoing);
        Assert.Single(await ReadRefundsAsync());
    }

    [Fact]
    public async Task A_partial_refund_refunds_that_much_off_the_single_paid_order()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 100m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        var refundId = Guid.NewGuid();

        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId, refundId, 30m, "usd"));

        var refund = Assert.Single(await ReadRefundsAsync());
        Assert.Equal(orderId, refund.PaymentOrderId);
        Assert.Equal(refundId, refund.RequestId);
        Assert.Equal(30m, refund.RefundAmount.Amount);
        Assert.Equal("USD", refund.RefundAmount.Currency);
        Assert.Equal(RefundOrderStatus.Pending, refund.Status);
        Assert.Equal(refund.RefundOrderId, Assert.Single(outgoing.OfType<ProcessRefund>()).RefundOrderId);
    }

    [Fact]
    public async Task A_redelivered_partial_refund_records_nothing_new()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 100m);
        var message = new RefundRequestedIntegrationEvent(checkout.BookingId, Guid.NewGuid(), 30m, "USD");
        await RequestAsync(message);

        var outgoing = await RequestAsync(message);

        Assert.Empty(outgoing);
        Assert.Single(await ReadRefundsAsync());
    }

    public static TheoryData<string, decimal, string, bool> RefusedPartials => new()
    {
        { "no refund id", 30m, "USD", false },
        { "more than is left", 100.01m, "USD", true },
        { "another currency", 30m, "EUR", true },
    };

    [Theory]
    [MemberData(nameof(RefusedPartials))]
    public async Task A_partial_refund_that_cannot_be_done_automatically_is_left_for_a_person(
        string _, decimal amount, string currency, bool withRefundId)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 100m);

        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(
            checkout.BookingId, withRefundId ? Guid.NewGuid() : null, amount, currency));

        Assert.Empty(outgoing);
        Assert.Empty(await ReadRefundsAsync());
        Assert.Contains(Logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }

    [Fact]
    public async Task A_partial_refund_of_a_booking_with_two_paid_orders_is_left_for_a_person()
    {
        var checkout = await SeedCheckoutWithOrdersAsync(Guid.NewGuid(), NewBookingId(),
            (OrderState.Success, 40m), (OrderState.Success, 60m));

        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId, Guid.NewGuid(), 10m, "USD"));

        Assert.Empty(outgoing);
        Assert.Empty(await ReadRefundsAsync());
        Assert.Contains(Logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }

    [Fact]
    public async Task A_partial_refund_of_a_booking_with_nothing_paid_is_left_for_a_person()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Failed, amount: 100m);

        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(checkout.BookingId, Guid.NewGuid(), 10m, "USD"));

        Assert.Empty(outgoing);
        Assert.Empty(await ReadRefundsAsync());
    }

    [Fact]
    public async Task A_refund_for_a_booking_with_no_checkout_records_nothing()
    {
        var outgoing = await RequestAsync(new RefundRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid()));

        Assert.Empty(outgoing);
        Assert.Empty(await ReadRefundsAsync());
    }
}
