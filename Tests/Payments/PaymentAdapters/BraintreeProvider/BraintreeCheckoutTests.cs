using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.BraintreeProvider;

public sealed class BraintreeCheckoutTests : IDisposable
{
    private readonly FakeHttpServer _braintree = new();

    public BraintreeCheckoutTests() => _braintree.Respond(_ => BraintreeWire.Xml(201, BraintreeWire.ClientToken("client_token_abc")));

    public void Dispose() => _braintree.Dispose();

    [Fact]
    public async Task Hands_back_a_client_token_and_no_reference_because_nothing_exists_at_Braintree_yet()
    {
        var session = await Gateways.Braintree(_braintree).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 10m, "EUR"));

        Assert.Null(session.ProviderReference);
        Assert.Equal("client_token_abc", session.ClientToken);
    }

    [Theory]
    [InlineData("EUR", Gateways.EurMerchantAccount)]
    [InlineData("eur", Gateways.EurMerchantAccount)]
    [InlineData("USD", Gateways.UsdMerchantAccount)]
    public async Task Asks_for_a_token_on_the_merchant_account_that_settles_in_the_order_s_currency(string currency, string account)
    {
        await Gateways.Braintree(_braintree).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 10m, currency));

        var request = Assert.Single(_braintree.Requests);
        Assert.Equal(BraintreeWire.MerchantPath("client_token"), request.Path);
        Assert.Contains($"<merchant-account-id>{account}</merchant-account-id>", request.Body);
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Refuses_a_currency_it_has_no_merchant_account_for_without_calling_Braintree(string? currency)
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Braintree(_braintree).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 10m, currency!)));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_braintree.Requests);
    }
}
