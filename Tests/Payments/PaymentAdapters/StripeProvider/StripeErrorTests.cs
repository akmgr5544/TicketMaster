using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

// The error kind decides whether PaymentSystem retries. Calling a permanent error transient retries
// forever; calling a transient one permanent fails a payment that would have gone through.
public sealed class StripeErrorTests : IDisposable
{
    private readonly FakeHttpServer _stripe = new();

    public void Dispose() => _stripe.Dispose();

    [Theory]
    [InlineData(400, "invalid_request_error", PaymentProviderErrorKind.InvalidRequest)]
    [InlineData(400, "idempotency_error", PaymentProviderErrorKind.InvalidRequest)]
    [InlineData(402, "card_error", PaymentProviderErrorKind.InvalidRequest)]
    [InlineData(401, "authentication_error", PaymentProviderErrorKind.Configuration)]
    [InlineData(403, "permission_error", PaymentProviderErrorKind.Configuration)]
    [InlineData(409, "invalid_request_error", PaymentProviderErrorKind.Transient)]
    [InlineData(429, "rate_limit_error", PaymentProviderErrorKind.Transient)]
    [InlineData(500, "api_error", PaymentProviderErrorKind.Transient)]
    [InlineData(502, "api_error", PaymentProviderErrorKind.Transient)]
    [InlineData(503, "api_error", PaymentProviderErrorKind.Transient)]
    public async Task Classifies_each_Stripe_error(int status, string type, PaymentProviderErrorKind expected)
    {
        _stripe.Respond(_ => StripeWire.Error(status, type));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(Checkout);

        Assert.Equal(expected, exception.Kind);
        Assert.Equal(PaymentProviderKind.Stripe, exception.Provider);
        Assert.Equal(expected == PaymentProviderErrorKind.Transient, exception.IsRetryable);
    }

    [Fact]
    public async Task A_server_error_is_retried_with_the_same_idempotency_key_so_it_cannot_charge_twice()
    {
        var calls = 0;
        _stripe.Respond(_ => Interlocked.Increment(ref calls) == 1
            ? new FakeResponse(500, """{"error":{"type":"api_error","message":"blip"}}""", "application/json")
            : StripeWire.Ok(StripeWire.Intent()));

        var session = await Checkout();

        Assert.Equal("pi_1", session.ProviderReference);
        var keys = _stripe.Requests.Select(request => request.Header("Idempotency-Key")).Distinct().ToArray();
        Assert.Equal(2, _stripe.Requests.Count);
        Assert.Single(keys);
    }

    [Fact]
    public async Task Stripe_being_unreachable_is_transient()
    {
        _stripe.Dispose();

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(Checkout);

        Assert.Equal(PaymentProviderErrorKind.Transient, exception.Kind);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public async Task A_response_that_is_not_Stripe_json_still_comes_back_as_a_provider_error(string body)
    {
        _stripe.Respond(_ => new FakeResponse(200, body, "application/json"));

        await Assert.ThrowsAsync<PaymentProviderException>(Checkout);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_dressed_up_as_a_provider_failure()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent()));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 5m, "EUR"), cancellation.Token));
    }

    private Task<CheckoutSession> Checkout() =>
        Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 5m, "EUR"));
}
