using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

public sealed class StripeCheckoutTests : IDisposable
{
    private readonly FakeHttpServer _stripe = new();

    public StripeCheckoutTests() => _stripe.Respond(_ => StripeWire.Ok(StripeWire.Intent(clientSecret: "pi_1_secret_abc")));

    public void Dispose() => _stripe.Dispose();

    [Fact]
    public async Task Creates_one_payment_intent_and_hands_back_its_id_and_client_secret()
    {
        var session = await Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 10.99m, "USD"));

        Assert.Equal("pi_1", session.ProviderReference);
        Assert.Equal("pi_1_secret_abc", session.ClientToken);
        var request = Assert.Single(_stripe.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/v1/payment_intents", request.Path);
    }

    [Fact]
    public async Task Sends_the_amount_in_minor_units_and_the_currency_in_lower_case()
    {
        await Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 10.99m, "USD"));

        var form = Assert.Single(_stripe.Requests).Form;
        Assert.Equal("1099", form["amount"]);
        Assert.Equal("usd", form["currency"]);
        Assert.Equal("true", form["automatic_payment_methods[enabled]"]);
    }

    [Fact]
    public async Task Tags_the_intent_with_the_order_so_a_lost_response_can_be_found_again()
    {
        var orderId = Guid.NewGuid();

        await Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(orderId, 5m, "EUR"));

        Assert.Equal(orderId.ToString(), Assert.Single(_stripe.Requests).Form[$"metadata[{StripeWire.OrderIdKey}]"]);
    }

    [Fact]
    public async Task A_redelivered_request_for_the_same_order_reuses_the_idempotency_key()
    {
        var orderId = Guid.NewGuid();
        var gateway = Gateways.Stripe(_stripe);

        await gateway.CreateCheckoutAsync(new CheckoutRequest(orderId, 5m, "EUR"));
        await gateway.CreateCheckoutAsync(new CheckoutRequest(orderId, 5m, "EUR"));

        var keys = _stripe.Requests.Select(request => request.Header("Idempotency-Key")).ToArray();
        Assert.Equal(2, keys.Length);
        Assert.NotNull(keys[0]);
        Assert.Equal(keys[0], keys[1]);
    }

    [Fact]
    public async Task Different_orders_never_share_an_idempotency_key()
    {
        var gateway = Gateways.Stripe(_stripe);

        await gateway.CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 5m, "EUR"));
        await gateway.CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 5m, "EUR"));

        var keys = _stripe.Requests.Select(request => request.Header("Idempotency-Key")).ToArray();
        Assert.NotEqual(keys[0], keys[1]);
    }

    [Fact]
    public async Task Refuses_an_empty_order_id_because_every_such_order_would_share_one_idempotency_key()
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(Guid.Empty, 5m, "EUR")));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_stripe.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("U$D")]
    public async Task Refuses_a_currency_that_is_not_an_ISO_code_without_calling_Stripe(string? currency)
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Gateways.Stripe(_stripe).CreateCheckoutAsync(new CheckoutRequest(Guid.NewGuid(), 5m, currency!)));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_stripe.Requests);
    }

    [Fact]
    public async Task Submitting_a_payment_method_is_a_no_op_because_the_client_confirms_with_Stripe_directly()
    {
        var result = await Gateways.Stripe(_stripe).SubmitPaymentMethodAsync(
            new SubmitPaymentMethodRequest(Guid.NewGuid(), 5m, "EUR", "pm_card_visa"));

        Assert.Null(result);
        Assert.Empty(_stripe.Requests);
    }
}
