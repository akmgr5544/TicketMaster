using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Messaging;

namespace PaymentIntegration.Features.RefundOrders;

public sealed class ProcessRefundTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<RecordRefundOutcome?> ProcessAsync(Guid refundOrderId) =>
        HandleAsync<ProcessRefundHandler, RecordRefundOutcome?>((handler, _, services) =>
            handler.Handle(new ProcessRefund(refundOrderId),
                services.GetRequiredService<PaymentQueryDbContext>(),
                services.GetRequiredService<IPaymentGatewayFactory>(),
                CancellationToken.None));

    [Fact]
    public async Task The_refund_goes_to_the_psp_that_took_the_payment_with_the_request_id_as_its_key()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 100m, provider: "Braintree");
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        var requestId = Guid.NewGuid();
        var refund = await SeedRefundAsync(orderId, 30m, requestId);

        var outcome = await ProcessAsync(refund.RefundOrderId);

        Assert.Empty(Psp.Stripe.Refunds);
        Assert.True(Psp.Braintree.Refunds.TryPeek(out var request));
        Assert.Equal(orderId, request.PaymentOrderId);
        Assert.Equal(StubGateway.ReferenceFor(orderId), request.ProviderReference);
        Assert.Equal(30m, request.Amount);
        Assert.Equal("USD", request.Currency);
        Assert.Equal(requestId, request.RefundId);

        Assert.NotNull(outcome);
        Assert.Equal(refund.RefundOrderId, outcome.RefundOrderId);
        Assert.True(outcome.Accepted);
        Assert.Equal(StubGateway.RefundReferenceFor(orderId, requestId), outcome.ProviderReference);
    }

    [Theory]
    [InlineData(RefundStatus.Succeeded, true)]
    [InlineData(RefundStatus.Pending, true)]
    [InlineData(RefundStatus.Failed, false)]
    public async Task The_psps_answer_decides_whether_the_refund_was_accepted(RefundStatus answer, bool accepted)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var refund = await SeedRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 10m);
        Psp.Stripe.OnRefund = _ => new RefundResult("re_answer", answer, answer == RefundStatus.Failed ? "declined" : null);

        var outcome = await ProcessAsync(refund.RefundOrderId);

        Assert.Equal(accepted, outcome!.Accepted);
    }

    [Fact]
    public async Task A_refund_the_psp_refuses_is_recorded_as_not_accepted()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var refund = await SeedRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 10m);
        Psp.Stripe.OnRefund = _ =>
            throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.InvalidRequest, "no charge");

        var outcome = await ProcessAsync(refund.RefundOrderId);

        Assert.False(outcome!.Accepted);
    }

    [Fact]
    public async Task A_psp_that_is_down_makes_the_message_fail_so_it_is_retried()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var refund = await SeedRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 10m);
        Psp.Stripe.OnRefund = _ =>
            throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Transient, "down");

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => ProcessAsync(refund.RefundOrderId));
        Assert.Equal(PaymentProviderErrorKind.Transient, exception.Kind);
    }

    [Fact]
    public async Task A_refund_that_was_already_answered_is_not_sent_again()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var refund = await SeedSettledRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 10m);

        var outcome = await ProcessAsync(refund.RefundOrderId);

        Assert.Null(outcome);
        Assert.Empty(Psp.Stripe.Refunds);
    }

    [Fact]
    public async Task Processing_reads_but_does_not_write()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var refund = await SeedRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 10m);

        await ProcessAsync(refund.RefundOrderId);

        var stored = await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId));
        Assert.Equal(PaymentSystem.Enums.RefundOrderStatus.Pending, stored.Status);
    }
}
