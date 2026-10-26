using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.Checkouts;

public sealed class CancelCheckoutTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> CancelAsync(long bookingId) =>
        HandleAsync<BookingCancelledHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(new BookingCancelledIntegrationEvent(bookingId), context, CancellationToken.None));

    [Theory]
    [InlineData(OrderState.Init)]
    [InlineData(OrderState.Executing)]
    public async Task Cancelling_an_unpaid_booking_fails_its_order_and_fails_the_payment(OrderState state)
    {
        var checkout = await SeedCheckoutAsync(state);

        var outgoing = await CancelAsync(checkout.BookingId);

        Assert.Equal(checkout.BookingId, Assert.Single(outgoing.OfType<BookingPaymentFailedIntegrationEvent>()).BookingId);
        var order = Assert.Single((await ReadCheckoutAsync(checkout.BookingId)).PaymentOrders);
        Assert.Equal(PaymentOrderStatus.Failed, order.Status);
        Assert.Empty(await ReadAsync(c => c.RefundOrders.ToListAsync()));
    }

    [Fact]
    public async Task Cancelling_a_paid_booking_refunds_the_whole_order_under_the_orders_own_id()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 80m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        var outgoing = await CancelAsync(checkout.BookingId);

        var refund = Assert.Single(await ReadAsync(c => c.RefundOrders.ToListAsync()));
        Assert.Equal(orderId, refund.PaymentOrderId);
        Assert.Equal(orderId, refund.RequestId);
        Assert.Equal(80m, refund.RefundAmount.Amount);
        Assert.Equal("USD", refund.RefundAmount.Currency);
        Assert.Equal(RefundOrderStatus.Pending, refund.Status);

        Assert.Equal(refund.RefundOrderId, Assert.Single(outgoing.OfType<ProcessRefund>()).RefundOrderId);
        Assert.Empty(outgoing.OfType<BookingPaymentFailedIntegrationEvent>());
        Assert.Equal(PaymentOrderStatus.Success, Assert.Single((await ReadCheckoutAsync(checkout.BookingId)).PaymentOrders).Status);
    }

    [Fact]
    public async Task Cancelling_a_paid_booking_whose_earlier_refund_failed_still_refunds_everything()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 100m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        var failed = await SeedRefundAsync(orderId, 100m);
        await InScopeAsync(async c =>
        {
            (await c.RefundOrders.SingleAsync(r => r.RefundOrderId == failed.RefundOrderId)).MarkAsFailed();
            await c.SaveChangesAsync();
        });

        await CancelAsync(checkout.BookingId);

        var refund = Assert.Single(await ReadAsync(c => c.RefundOrders.Where(r => r.RequestId == orderId).ToListAsync()));
        Assert.Equal(100m, refund.RefundAmount.Amount);
    }

    [Fact]
    public async Task A_redelivered_cancellation_of_a_paid_booking_sends_nothing_and_refunds_nothing_twice()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 50m);
        await CancelAsync(checkout.BookingId);

        var outgoing = await CancelAsync(checkout.BookingId);

        Assert.Empty(outgoing);
        Assert.Single(await ReadAsync(c => c.RefundOrders.ToListAsync()));
    }

    [Fact]
    public async Task A_redelivered_cancellation_of_an_unpaid_booking_fails_nothing_and_sends_nothing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        await CancelAsync(checkout.BookingId);

        var outgoing = await CancelAsync(checkout.BookingId);

        Assert.Empty(outgoing);
    }

    // The cancellation overtook the payment request; the claim is what makes the request refuse when it lands.
    [Fact]
    public async Task Cancelling_a_booking_that_has_no_checkout_claims_it_cancelled_and_sends_nothing()
    {
        var bookingId = NewBookingId();

        var outgoing = await CancelAsync(bookingId);

        Assert.Empty(outgoing);
        Assert.Empty(await ReadAsync(c => c.PaymentEvents.ToListAsync()));
        var claim = await ReadAsync(c => c.BookingClaims.SingleAsync(x => x.BookingId == bookingId));
        Assert.Equal(BookingClaimStatus.Cancelled, claim.Status);
    }

    [Fact]
    public async Task A_redelivered_early_cancellation_records_nothing_more_and_sends_nothing()
    {
        var bookingId = NewBookingId();
        await CancelAsync(bookingId);

        var outgoing = await CancelAsync(bookingId);

        Assert.Empty(outgoing);
        Assert.Equal(1, await ReadAsync(c => c.BookingClaims.CountAsync(x => x.BookingId == bookingId)));
    }
}
