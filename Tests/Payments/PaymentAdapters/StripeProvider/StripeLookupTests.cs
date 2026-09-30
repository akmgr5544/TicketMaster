using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

public sealed class StripeLookupTests : IDisposable
{
    private readonly FakeHttpServer _stripe = new();

    public void Dispose() => _stripe.Dispose();

    [Theory]
    [InlineData("requires_payment_method", PaymentStatus.AwaitingPaymentMethod)]
    [InlineData("requires_confirmation", PaymentStatus.AwaitingPaymentMethod)]
    [InlineData("requires_action", PaymentStatus.RequiresAction)]
    [InlineData("processing", PaymentStatus.Processing)]
    [InlineData("requires_capture", PaymentStatus.Authorized)]
    [InlineData("succeeded", PaymentStatus.Succeeded)]
    [InlineData("canceled", PaymentStatus.Canceled)]
    public async Task Maps_every_intent_status(string stripeStatus, PaymentStatus expected)
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent(status: stripeStatus)));

        var result = await Lookup("pi_1");

        Assert.NotNull(result);
        Assert.Equal(expected, result.Status);
        Assert.Equal("pi_1", result.ProviderReference);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task A_declined_attempt_is_a_failure_with_the_reason_even_though_the_intent_is_reusable()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent(status: "requires_payment_method", lastError: "Your card was declined.")));

        var result = await Lookup("pi_1");

        Assert.Equal(PaymentStatus.Failed, result!.Status);
        Assert.Equal("Your card was declined.", result.FailureReason);
    }

    [Fact]
    public async Task A_succeeded_intent_is_not_reported_as_failed_because_an_earlier_attempt_was_declined()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent(status: "succeeded", lastError: "Your card was declined.")));

        var result = await Lookup("pi_1");

        Assert.Equal(PaymentStatus.Succeeded, result!.Status);
    }

    [Fact]
    public async Task A_status_Stripe_adds_later_is_surfaced_not_guessed()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent(status: "requires_something_new")));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Lookup("pi_1"));

        Assert.Equal(PaymentProviderErrorKind.Unknown, exception.Kind);
    }

    [Fact]
    public async Task Reads_by_reference_when_it_has_one()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent(id: "pi_42")));

        await Lookup("pi_42");

        var request = Assert.Single(_stripe.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/v1/payment_intents/pi_42", request.Path);
    }

    [Fact]
    public async Task An_intent_Stripe_does_not_know_is_no_payment_rather_than_an_error()
    {
        _stripe.Respond(_ => StripeWire.Error(404, "invalid_request_error", "resource_missing"));

        Assert.Null(await Lookup("pi_missing"));
    }

    [Fact]
    public async Task Without_a_reference_searches_by_the_order_id_it_tagged_the_intent_with()
    {
        var orderId = Guid.NewGuid();
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Search(StripeWire.Intent(orderId: orderId))));

        var result = await Gateways.Stripe(_stripe).LookupAsync(new PaymentLookupRequest(orderId, null));

        Assert.Equal("pi_1", result!.ProviderReference);
        var request = Assert.Single(_stripe.Requests);
        Assert.Equal("/v1/payment_intents/search", request.Path);
        Assert.Equal($"metadata['{StripeWire.OrderIdKey}']:'{orderId}'", request.QueryString["query"]);
    }

    [Fact]
    public async Task When_a_search_finds_several_intents_the_newest_one_wins_whatever_order_they_come_in()
    {
        var orderId = Guid.NewGuid();
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Search(
            StripeWire.Intent(id: "pi_old", status: "canceled", orderId: orderId, created: 1_000),
            StripeWire.Intent(id: "pi_new", status: "succeeded", orderId: orderId, created: 3_000),
            StripeWire.Intent(id: "pi_mid", status: "canceled", orderId: orderId, created: 2_000))));

        var result = await Gateways.Stripe(_stripe).LookupAsync(new PaymentLookupRequest(orderId, null));

        Assert.Equal("pi_new", result!.ProviderReference);
        Assert.Equal(PaymentStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_is_no_payment()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Search()));

        Assert.Null(await Gateways.Stripe(_stripe).LookupAsync(new PaymentLookupRequest(Guid.NewGuid(), null)));
    }

    private Task<PaymentResult?> Lookup(string reference) =>
        Gateways.Stripe(_stripe).LookupAsync(new PaymentLookupRequest(Guid.NewGuid(), reference));
}
