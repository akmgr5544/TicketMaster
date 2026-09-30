using System.Globalization;
using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

// Stripe charges integer minor units whose exponent depends on the currency. A wrong factor charges
// the customer 100x too much or too little, so every class of currency is pinned here, and every
// rejection is checked to happen before anything reaches Stripe.
public sealed class StripeAmountTests : IDisposable
{
    private readonly FakeHttpServer _stripe = new();

    public StripeAmountTests() => _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent()));

    public void Dispose() => _stripe.Dispose();

    [Theory]
    [InlineData("10.99", "USD", "1099")]
    [InlineData("0.01", "EUR", "1")]
    [InlineData("0.5", "GBP", "50")]
    [InlineData("100", "USD", "10000")]
    [InlineData("1000", "JPY", "1000")]
    [InlineData("1000", "jpy", "1000")]
    [InlineData("25000", "KRW", "25000")]
    [InlineData("5", "ISK", "500")]
    [InlineData("3000", "UGX", "300000")]
    [InlineData("1.23", "KWD", "1230")]
    [InlineData("1.2", "BHD", "1200")]
    [InlineData("10.990000", "USD", "1099")]
    public async Task Converts_to_the_currency_s_minor_units(string amount, string currency, string expected)
    {
        await Checkout(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);

        Assert.Equal(expected, Assert.Single(_stripe.Requests).Form["amount"]);
    }

    [Theory]
    [InlineData("0", "USD")]
    [InlineData("-10", "USD")]
    [InlineData("-0.01", "EUR")]
    [InlineData("10.999", "USD")]
    [InlineData("0.001", "USD")]
    [InlineData("10.5", "JPY")]
    [InlineData("5.5", "ISK")]
    [InlineData("5.01", "UGX")]
    [InlineData("1.235", "KWD")]
    [InlineData("1.001", "KWD")]
    public async Task Refuses_an_amount_the_currency_cannot_express_instead_of_rounding_it(string amount, string currency)
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Checkout(decimal.Parse(amount, CultureInfo.InvariantCulture), currency));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_stripe.Requests);
    }

    [Theory]
    [InlineData("100000000000000000")]
    [InlineData("79228162514264337593543950335")]
    public async Task An_absurd_amount_is_an_invalid_request_not_an_overflow(string amount)
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Checkout(decimal.Parse(amount, CultureInfo.InvariantCulture), "USD"));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_stripe.Requests);
    }

    [Fact]
    public async Task The_amount_on_the_wire_does_not_depend_on_the_server_s_culture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            await Checkout(1234.56m, "EUR");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Equal("123456", Assert.Single(_stripe.Requests).Form["amount"]);
    }

    private Task<CheckoutSession> Checkout(decimal amount, string currency) =>
        Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), amount, currency));
}
