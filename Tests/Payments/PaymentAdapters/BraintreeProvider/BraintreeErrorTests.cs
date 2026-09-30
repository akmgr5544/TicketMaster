using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.BraintreeProvider;

// The error kind decides whether PaymentSystem retries. Calling a permanent error transient retries
// forever; calling a transient one permanent fails a payment that would have gone through.
public sealed class BraintreeErrorTests : IDisposable
{
    private readonly FakeHttpServer _braintree = new();

    public void Dispose() => _braintree.Dispose();

    [Theory]
    [InlineData(401, PaymentProviderErrorKind.Configuration)]
    [InlineData(403, PaymentProviderErrorKind.Configuration)]
    [InlineData(426, PaymentProviderErrorKind.Configuration)]
    [InlineData(408, PaymentProviderErrorKind.Transient)]
    [InlineData(429, PaymentProviderErrorKind.Transient)]
    [InlineData(500, PaymentProviderErrorKind.Transient)]
    [InlineData(503, PaymentProviderErrorKind.Transient)]
    [InlineData(504, PaymentProviderErrorKind.Transient)]
    public async Task Classifies_each_Braintree_error(int status, PaymentProviderErrorKind expected)
    {
        _braintree.Respond(_ => new FakeResponse(status, "", "application/xml"));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(Checkout);

        Assert.Equal(expected, exception.Kind);
        Assert.Equal(PaymentProviderKind.Braintree, exception.Provider);
    }

    [Fact]
    public async Task A_sale_that_hits_a_server_error_is_transient_so_the_caller_looks_before_charging_again()
    {
        _braintree.Respond(_ => new FakeResponse(500, "", "application/xml"));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Braintree(_braintree).SubmitPaymentMethodAsync(new SubmitPaymentMethodRequest(Guid.NewGuid(), 10m, "EUR", "nonce")));

        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public async Task Braintree_being_unreachable_is_transient()
    {
        _braintree.Dispose();

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(Checkout);

        Assert.Equal(PaymentProviderErrorKind.Transient, exception.Kind);
    }

    [Theory]
    [InlineData("this is not xml")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public async Task A_response_that_is_not_Braintree_xml_still_comes_back_as_a_provider_error(string body)
    {
        _braintree.Respond(_ => new FakeResponse(200, body, "application/xml"));

        await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Braintree(_braintree).LookupAsync(new PaymentLookupRequest(Guid.NewGuid(), "tx_1")));
    }

    private Task<CheckoutSession> Checkout() =>
        Gateways.Braintree(_braintree).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 10m, "EUR"));
}
