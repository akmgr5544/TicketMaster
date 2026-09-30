using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Enums;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class StartCheckoutTests(PaymentsFixture fixture) : PspTest(fixture)
{
    private static Dictionary<string, string> Route(Guid paymentOrderId) =>
        new() { ["paymentOrderId"] = paymentOrderId.ToString() };

    [Fact]
    public async Task Starts_the_order_with_the_provider_reference_and_returns_the_client_token()
    {
        var checkout = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 42.10m, "EUR"));
        var id = checkout.OrderId(0);

        var result = await SendAsync(new StartCheckout.Command(checkout.BuyerId, id));

        Assert.True(result.IsSuccess);
        Assert.Equal(id, result.Value!.PaymentOrderId);
        Assert.Equal("Stripe", result.Value.Provider);
        Assert.Equal(StubGateway.ClientTokenFor(id, 1), result.Value.ClientToken);

        var stored = await ReadOrderAsync(id);
        Assert.Equal(PaymentOrderStatus.Executing, stored.Status);
        Assert.Equal(StubGateway.ReferenceFor(id), stored.PspToken);

        // The order id is the idempotency nonce, and the amount is the order's, not anything the client said.
        var sent = Assert.Single(Psp.Stripe.Checkouts);
        Assert.Equal(new CheckoutRequest(id, 42.10m, "EUR"), sent);
    }

    [Fact]
    public async Task A_provider_with_no_reference_until_payment_starts_the_order_without_one()
    {
        Psp.DefaultKind = PaymentProviderKind.Braintree;
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var result = await SendAsync(new StartCheckout.Command(checkout.BuyerId, checkout.OrderId(0)));

        Assert.True(result.IsSuccess);
        Assert.Equal("Braintree", result.Value!.Provider);
        var stored = await OrderAsync(checkout, 0);
        Assert.Equal(PaymentOrderStatus.Executing, stored.Status);
        Assert.Null(stored.PspToken);
    }

    [Fact]
    public async Task Only_the_asked_for_order_starts()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.NotStarted);

        await SendAsync(new StartCheckout.Command(checkout.BuyerId, checkout.OrderId(1)));

        Assert.Equal(PaymentOrderStatus.NotStarted, (await OrderAsync(checkout, 0)).Status);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 1)).Status);
    }

    [Fact]
    public async Task Another_buyers_order_is_not_found_and_the_provider_is_never_asked()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var result = await SendAsync(new StartCheckout.Command(Guid.NewGuid(), checkout.OrderId(0)));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("payment_order_not_found", result.Error.Code);
        Assert.Empty(Psp.Stripe.Checkouts);
        Assert.Equal(PaymentOrderStatus.NotStarted, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task Another_buyers_order_and_a_missing_one_are_indistinguishable()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        var intruder = Guid.NewGuid();

        var someoneElses = await SendAsync(new StartCheckout.Command(intruder, checkout.OrderId(0)));
        var missing = await SendAsync(new StartCheckout.Command(intruder, Guid.NewGuid()));

        Assert.Equal(missing.Error!.Type, someoneElses.Error!.Type);
        Assert.Equal(missing.Error.Code, someoneElses.Error.Code);
    }

    [Fact]
    public async Task An_unknown_order_is_not_found()
    {
        var result = await SendAsync(new StartCheckout.Command(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(Psp.Stripe.Checkouts);
    }

    [Theory]
    [InlineData(OrderState.Success)]
    [InlineData(OrderState.Failed)]
    public async Task A_settled_order_is_a_conflict_and_the_provider_is_never_asked(OrderState state)
    {
        var checkout = await SeedCheckoutAsync(state);

        var result = await SendAsync(new StartCheckout.Command(checkout.BuyerId, checkout.OrderId(0)));

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("payment_order_settled", result.Error.Code);
        Assert.Empty(Psp.Stripe.Checkouts);
    }

    [Fact]
    public async Task A_redelivered_checkout_replays_the_same_session_and_records_nothing_new()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        var id = checkout.OrderId(0);
        await SendAsync(new StartCheckout.Command(checkout.BuyerId, id));
        var afterFirst = await ReadCheckoutAsync(checkout.CheckoutId);

        var again = await SendAsync(new StartCheckout.Command(checkout.BuyerId, id));

        Assert.True(again.IsSuccess);
        // The client token is never stored, so the provider is asked again — with the same idempotency nonce.
        Assert.Equal(StubGateway.ClientTokenFor(id, 2), again.Value!.ClientToken);
        Assert.All(Psp.Stripe.Checkouts, request => Assert.Equal(id, request.PaymentOrderId));
        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.Equal(StubGateway.ReferenceFor(id), stored.Order(id).PspToken);
        Assert.Equal(afterFirst.Version, stored.Version);
    }

    [Fact]
    public async Task A_redelivered_checkout_that_opens_a_different_session_is_refused_and_keeps_the_first()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        var id = checkout.OrderId(0);
        await SendAsync(new StartCheckout.Command(checkout.BuyerId, id));
        // An expired idempotency key: the provider no longer replays and opens a second payment.
        Psp.Stripe.OnCheckout = _ => new CheckoutSession("pi_second", "client_secret_second");

        var again = await SendAsync(new StartCheckout.Command(checkout.BuyerId, id));

        Assert.Equal(ErrorType.Conflict, again.Error!.Type);
        Assert.Equal("psp_session_mismatch", again.Error.Code);
        Assert.Equal(StubGateway.ReferenceFor(id), (await ReadOrderAsync(id)).PspToken);
    }

    [Fact]
    public async Task A_redelivered_checkout_on_a_provider_without_references_hands_out_a_fresh_token()
    {
        Psp.DefaultKind = PaymentProviderKind.Braintree;
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        var id = checkout.OrderId(0);
        await SendAsync(new StartCheckout.Command(checkout.BuyerId, id));

        var again = await SendAsync(new StartCheckout.Command(checkout.BuyerId, id));

        Assert.True(again.IsSuccess);
        Assert.Equal(PaymentOrderStatus.Executing, (await ReadOrderAsync(id)).Status);
    }

    [Theory]
    [InlineData(PaymentProviderErrorKind.InvalidRequest, ErrorType.BadRequest, "psp_rejected_request")]
    [InlineData(PaymentProviderErrorKind.Transient, ErrorType.Conflict, "psp_unavailable")]
    public async Task A_provider_failure_the_caller_can_act_on_is_translated_and_starts_nothing(
        PaymentProviderErrorKind kind, ErrorType expected, string code)
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        Psp.Stripe.OnCheckout = _ => throw new PaymentProviderException(PaymentProviderKind.Stripe, kind, "boom");

        var result = await SendAsync(new StartCheckout.Command(checkout.BuyerId, checkout.OrderId(0)));

        Assert.Equal(expected, result.Error!.Type);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(PaymentOrderStatus.NotStarted, (await OrderAsync(checkout, 0)).Status);
    }

    [Theory]
    [InlineData(PaymentProviderErrorKind.Configuration)]
    [InlineData(PaymentProviderErrorKind.Unknown)]
    public async Task A_provider_fault_of_this_services_own_making_is_not_blamed_on_the_caller(PaymentProviderErrorKind kind)
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        Psp.Stripe.OnCheckout = _ => throw new PaymentProviderException(PaymentProviderKind.Stripe, kind, "boom");

        await Assert.ThrowsAsync<PaymentProviderException>(
            () => SendAsync(new StartCheckout.Command(checkout.BuyerId, checkout.OrderId(0))));
        Assert.Equal(PaymentOrderStatus.NotStarted, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task A_reference_the_order_cannot_hold_is_refused_and_starts_nothing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        Psp.Stripe.OnCheckout = _ => new CheckoutSession(new string('r', 201), "client_secret");

        var result = await SendAsync(new StartCheckout.Command(checkout.BuyerId, checkout.OrderId(0)));

        Assert.Equal(ErrorType.BadRequest, result.Error!.Type);
        Assert.Equal(PaymentOrderStatus.NotStarted, (await OrderAsync(checkout, 0)).Status);
    }

    // --- The endpoint ---

    [Fact]
    public async Task Without_the_identity_header_the_endpoint_is_401_and_the_provider_is_never_asked()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var response = await Endpoint(new StartCheckoutEndpoints()).PostAsync(Route(checkout.OrderId(0)));

        Assert.Equal(401, response.Status);
        Assert.Empty(Psp.Stripe.Checkouts);
    }

    [Fact]
    public async Task A_malformed_identity_header_is_401()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var response = await Endpoint(new StartCheckoutEndpoints()).PostAsync(Route(checkout.OrderId(0)),
            headers: new Dictionary<string, string> { ["X-Identity-UserId"] = "not-a-guid" });

        Assert.Equal(401, response.Status);
    }

    [Fact]
    public async Task Another_buyers_order_is_404_at_the_endpoint_never_403()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var response = await Endpoint(new StartCheckoutEndpoints())
            .PostAsync(Route(checkout.OrderId(0)), headers: As(Guid.NewGuid()));

        Assert.Equal(404, response.Status);
    }

    [Fact]
    public async Task The_endpoint_returns_the_client_token_and_nothing_else_secret()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        var id = checkout.OrderId(0);

        var response = await Endpoint(new StartCheckoutEndpoints()).PostAsync(Route(id), headers: As(checkout.BuyerId));

        Assert.Equal(200, response.Status);
        Assert.Contains(StubGateway.ClientTokenFor(id, 1), response.Body);
        Assert.DoesNotContain(StubGateway.ReferenceFor(id), response.Body);
    }

    [Fact]
    public async Task Neither_the_client_token_nor_the_provider_reference_is_ever_logged()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);
        var id = checkout.OrderId(0);

        await Endpoint(new StartCheckoutEndpoints()).PostAsync(Route(id), headers: As(checkout.BuyerId));
        // The mismatch path logs a warning about the session, so it is the one most tempted to print it.
        Psp.Stripe.OnCheckout = _ => new CheckoutSession("pi_second", "client_secret_second");
        await Endpoint(new StartCheckoutEndpoints()).PostAsync(Route(id), headers: As(checkout.BuyerId));

        Assert.Contains(Logs.Lines, line => line.Contains(id.ToString()));
        foreach (var secret in new[] { StubGateway.ClientTokenFor(id, 1), "client_secret_second", StubGateway.ReferenceFor(id), "pi_second" })
            Assert.DoesNotContain(Logs.Lines, line => line.Contains(secret));
    }
}
