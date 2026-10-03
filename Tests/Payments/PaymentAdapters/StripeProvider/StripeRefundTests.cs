using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

public sealed class StripeRefundTests : IDisposable
{
    private readonly FakeHttpServer _stripe = new();

    public void Dispose() => _stripe.Dispose();

    [Fact]
    public async Task Refunds_the_intent_in_minor_units_keyed_on_the_order_and_the_refund()
    {
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        _stripe.Respond(request => request.Method == "GET"
            ? StripeWire.Ok(StripeWire.Intent(status: "succeeded"))
            : StripeWire.Ok(StripeWire.Refund()));

        var result = await Gateways.Stripe(_stripe).RefundAsync(new RefundRequest(orderId, "pi_1", 10.99m, "USD", refundId));

        Assert.Equal(new RefundResult("re_1", RefundStatus.Succeeded), result);
        var refund = _stripe.Requests[1];
        Assert.Equal("POST", refund.Method);
        Assert.Equal("/v1/refunds", refund.Path);
        Assert.Equal("pi_1", refund.Form["payment_intent"]);
        Assert.Equal("1099", refund.Form["amount"]);
        Assert.Equal(orderId.ToString(), refund.Form[$"metadata[{StripeWire.OrderIdKey}]"]);
        Assert.Equal(refundId.ToString(), refund.Form[$"metadata[{StripeWire.RefundIdKey}]"]);
        // Keyed on the refund too: an order refunded in parts takes one refund per part.
        Assert.Equal($"refund:{orderId}:{refundId}", refund.Header("Idempotency-Key"));
    }

    [Theory]
    [InlineData("pending", RefundStatus.Pending)]
    [InlineData("requires_action", RefundStatus.Pending)]
    [InlineData("succeeded", RefundStatus.Succeeded)]
    [InlineData("failed", RefundStatus.Failed)]
    [InlineData("canceled", RefundStatus.Failed)]
    public async Task Maps_every_refund_status(string stripeStatus, RefundStatus expected)
    {
        _stripe.Respond(request => request.Method == "GET"
            ? StripeWire.Ok(StripeWire.Intent(status: "succeeded"))
            : StripeWire.Ok(StripeWire.Refund(status: stripeStatus)));

        var result = await Gateways.Stripe(_stripe).RefundAsync(new RefundRequest(Guid.NewGuid(), "pi_1", 10.99m, "USD", Guid.NewGuid()));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Without_a_reference_finds_the_intent_by_the_order_id()
    {
        var orderId = Guid.NewGuid();
        _stripe.Respond(request => request.Path.EndsWith("/search", StringComparison.Ordinal)
            ? StripeWire.Ok(StripeWire.Search(StripeWire.Intent(id: "pi_found", status: "succeeded", orderId: orderId)))
            : StripeWire.Ok(StripeWire.Refund()));

        await Gateways.Stripe(_stripe).RefundAsync(new RefundRequest(orderId, null, 10.99m, "USD", Guid.NewGuid()));

        Assert.Equal("pi_found", _stripe.Requests[1].Form["payment_intent"]);
    }

    [Fact]
    public async Task An_intent_that_never_succeeded_is_refused_without_asking_for_a_refund()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent(status: "canceled")));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Stripe(_stripe).RefundAsync(new RefundRequest(Guid.NewGuid(), "pi_1", 10.99m, "USD", Guid.NewGuid())));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Single(_stripe.Requests);
    }

    // Past the idempotency key's 24 hours Stripe refuses a refund that would take the intent past its amount. If
    // this refund is among those already made, it is the answer, so a late redelivery reports success instead of
    // failing for ever.
    [Fact]
    public async Task An_intent_already_refunded_returns_the_existing_refund_for_this_refund_id()
    {
        var refundId = Guid.NewGuid();
        _stripe.Respond(request => (request.Method, request.Path) switch
        {
            ("GET", "/v1/refunds") => StripeWire.Ok(StripeWire.List(
                StripeWire.Refund(id: "re_mine", created: 1_000, refundId: refundId),
                StripeWire.Refund(id: "re_other", created: 2_000, refundId: Guid.NewGuid()))),
            ("GET", _) => StripeWire.Ok(StripeWire.Intent(status: "succeeded")),
            _ => StripeWire.Error(400, "invalid_request_error", "charge_already_refunded"),
        });

        var result = await Gateways.Stripe(_stripe).RefundAsync(
            new RefundRequest(Guid.NewGuid(), "pi_1", 10.99m, "USD", refundId));

        Assert.Equal(new RefundResult("re_mine", RefundStatus.Succeeded), result);
        Assert.Equal("pi_1", _stripe.Requests[2].QueryString["payment_intent"]);
    }

    // Other refunds used the intent up, and none of them is this one: there is nothing left for it.
    [Fact]
    public async Task An_intent_refunded_by_other_refunds_refuses_this_one()
    {
        _stripe.Respond(request => (request.Method, request.Path) switch
        {
            ("GET", "/v1/refunds") => StripeWire.Ok(StripeWire.List(StripeWire.Refund(id: "re_other", refundId: Guid.NewGuid()))),
            ("GET", _) => StripeWire.Ok(StripeWire.Intent(status: "succeeded")),
            _ => StripeWire.Error(400, "invalid_request_error", "charge_already_refunded"),
        });

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Stripe(_stripe).RefundAsync(new RefundRequest(Guid.NewGuid(), "pi_1", 10.99m, "USD", Guid.NewGuid())));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
    }

    [Fact]
    public async Task A_refund_status_Stripe_adds_later_is_surfaced_not_guessed()
    {
        _stripe.Respond(request => request.Method == "GET"
            ? StripeWire.Ok(StripeWire.Intent(status: "succeeded"))
            : StripeWire.Ok(StripeWire.Refund(status: "something_new")));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Stripe(_stripe).RefundAsync(new RefundRequest(Guid.NewGuid(), "pi_1", 10.99m, "USD", Guid.NewGuid())));

        Assert.Equal(PaymentProviderErrorKind.Unknown, exception.Kind);
    }
}
