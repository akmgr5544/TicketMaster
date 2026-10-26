using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

// A refund Stripe accepted as pending is followed up by reading it again: its status then is the answer.
public sealed class StripeRefundLookupTests : IDisposable
{
    private readonly FakeHttpServer _stripe = new();

    public void Dispose() => _stripe.Dispose();

    [Fact]
    public async Task Reads_the_refund_by_its_reference()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Refund(id: "re_42", status: "succeeded")));

        var result = await Lookup("re_42");

        Assert.Equal(new RefundResult("re_42", RefundStatus.Succeeded), result);
        var request = Assert.Single(_stripe.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/v1/refunds/re_42", request.Path);
    }

    [Theory]
    [InlineData("pending", RefundStatus.Pending)]
    [InlineData("requires_action", RefundStatus.Pending)]
    [InlineData("succeeded", RefundStatus.Succeeded)]
    [InlineData("failed", RefundStatus.Failed)]
    [InlineData("canceled", RefundStatus.Failed)]
    public async Task Maps_every_refund_status(string stripeStatus, RefundStatus expected)
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Refund(status: stripeStatus)));

        var result = await Lookup("re_1");

        Assert.Equal(expected, result!.Status);
    }

    [Fact]
    public async Task A_failed_refund_carries_stripes_reason()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Refund(status: "failed", failureReason: "expired_or_canceled_card")));

        var result = await Lookup("re_1");

        Assert.Equal(new RefundResult("re_1", RefundStatus.Failed, "expired_or_canceled_card"), result);
    }

    [Fact]
    public async Task A_refund_Stripe_does_not_know_is_no_refund_rather_than_an_error()
    {
        _stripe.Respond(_ => StripeWire.Error(404, "invalid_request_error", "resource_missing"));

        Assert.Null(await Lookup("re_missing"));
    }

    [Fact]
    public async Task Stripe_being_down_is_transient()
    {
        _stripe.Respond(_ => StripeWire.Error(503, "api_error"));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Lookup("re_1"));

        Assert.Equal(PaymentProviderErrorKind.Transient, exception.Kind);
    }

    private Task<RefundResult?> Lookup(string reference) =>
        Gateways.Stripe(_stripe).LookupRefundAsync(new RefundLookupRequest(Guid.NewGuid(), reference));
}
