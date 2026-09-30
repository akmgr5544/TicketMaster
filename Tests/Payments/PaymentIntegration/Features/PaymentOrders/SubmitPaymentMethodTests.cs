using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Domain.Events;
using PaymentSystem.Enums;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class SubmitPaymentMethodTests : PspTest
{
    private const string Nonce = "fake-valid-nonce";

    public SubmitPaymentMethodTests(PaymentsFixture fixture) : base(fixture) =>
        Psp.DefaultKind = PaymentProviderKind.Braintree;

    private static Dictionary<string, string> Route(Guid paymentOrderId) =>
        new() { ["paymentOrderId"] = paymentOrderId.ToString() };

    private void BraintreeAnswers(PaymentStatus status, string? reason = null) =>
        Psp.Braintree.OnSubmit = request =>
            Task.FromResult<PaymentResult?>(new PaymentResult($"bt_{request.PaymentOrderId:N}", status, reason));

    [Fact]
    public async Task A_synchronous_success_settles_the_order_and_the_checkout()
    {
        var checkout = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 12.34m, "GBP"), OrderState.Executing);
        var id = checkout.OrderId(0);

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, id, Nonce));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Pending);
        Assert.Equal("Succeeded", result.Value.ProviderStatus);
        Assert.Equal("Success", result.Value.OrderStatus);
        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Success, stored.Order(id).Status);
        Assert.True(stored.IsPaymentDone);
        Assert.Single(Events.Published, e => e.Event is PaymentOrderSucceededDomainEvent);

        // The amount charged is the order's, and the order id is what a timed-out sale is found by.
        Assert.Equal(new SubmitPaymentMethodRequest(id, 12.34m, "GBP", Nonce), Assert.Single(Psp.Braintree.Submissions));
    }

    [Fact]
    public async Task A_declined_charge_leaves_the_order_open_for_another_payment_method()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        BraintreeAnswers(PaymentStatus.Failed, "Do Not Honor");

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), Nonce));

        Assert.True(result.IsSuccess);
        Assert.Equal("Failed", result.Value!.ProviderStatus);
        Assert.Equal("Do Not Honor", result.Value.FailureReason);
        Assert.Equal("Executing", result.Value.OrderStatus);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task A_canceled_charge_fails_the_order()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        BraintreeAnswers(PaymentStatus.Canceled);

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), Nonce));

        Assert.Equal("Failed", result.Value!.OrderStatus);
        Assert.Equal(PaymentOrderStatus.Failed, (await OrderAsync(checkout, 0)).Status);
        Assert.Single(Events.Published, e => e.Event is PaymentOrderFailedDomainEvent);
    }

    [Theory]
    [InlineData(PaymentStatus.AwaitingPaymentMethod)]
    [InlineData(PaymentStatus.RequiresAction)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Authorized)]
    public async Task A_non_final_charge_status_leaves_the_order_executing(PaymentStatus status)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        BraintreeAnswers(status);

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), Nonce));

        Assert.Equal(status.ToString(), result.Value!.ProviderStatus);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task A_provider_that_reports_by_webhook_leaves_the_order_executing_and_answers_202()
    {
        Psp.DefaultKind = PaymentProviderKind.Stripe;
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var response = await Endpoint(new SubmitPaymentMethodEndpoints()).PostAsync(Route(checkout.OrderId(0)),
            JsonSerializer.Serialize(new { paymentMethod = Nonce }), As(checkout.BuyerId));

        Assert.Equal(202, response.Status);
        Assert.Contains("\"pending\":true", response.Body);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task A_synchronous_result_is_200_at_the_endpoint()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var response = await Endpoint(new SubmitPaymentMethodEndpoints()).PostAsync(Route(checkout.OrderId(0)),
            JsonSerializer.Serialize(new { paymentMethod = Nonce }), As(checkout.BuyerId));

        Assert.Equal(200, response.Status);
        Assert.Contains("\"orderStatus\":\"Success\"", response.Body);
        Assert.DoesNotContain($"bt_{checkout.OrderId(0):N}", response.Body);
    }

    [Fact]
    public async Task Without_the_identity_header_the_endpoint_is_401_and_nothing_is_charged()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var response = await Endpoint(new SubmitPaymentMethodEndpoints()).PostAsync(Route(checkout.OrderId(0)),
            JsonSerializer.Serialize(new { paymentMethod = Nonce }));

        Assert.Equal(401, response.Status);
        Assert.Empty(Psp.Braintree.Submissions);
    }

    [Fact]
    public async Task Another_buyers_order_is_not_found_and_nothing_is_charged()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await SendAsync(new SubmitPaymentMethod.Command(Guid.NewGuid(), checkout.OrderId(0), Nonce));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(Psp.Braintree.Submissions);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task An_unknown_order_is_not_found()
    {
        var result = await SendAsync(new SubmitPaymentMethod.Command(Guid.NewGuid(), Guid.NewGuid(), Nonce));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task An_order_without_a_checkout_is_a_conflict_and_nothing_is_charged()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), Nonce));

        Assert.Equal("checkout_not_started", result.Error!.Code);
        Assert.Empty(Psp.Braintree.Submissions);
    }

    [Theory]
    [InlineData(OrderState.Success)]
    [InlineData(OrderState.Failed)]
    public async Task A_settled_order_is_never_charged_again(OrderState state)
    {
        var checkout = await SeedCheckoutAsync(state);

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), Nonce));

        Assert.Equal("payment_order_settled", result.Error!.Code);
        Assert.Empty(Psp.Braintree.Submissions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_blank_payment_method_is_a_bad_request(string method)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), method));

        Assert.Equal(ErrorType.BadRequest, result.Error!.Type);
        Assert.Empty(Psp.Braintree.Submissions);
    }

    [Theory]
    [InlineData(PaymentProviderErrorKind.InvalidRequest, ErrorType.BadRequest)]
    [InlineData(PaymentProviderErrorKind.Transient, ErrorType.Conflict)]
    public async Task A_provider_failure_is_translated_and_leaves_the_order_executing(
        PaymentProviderErrorKind kind, ErrorType expected)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        Psp.Braintree.OnSubmit = _ => throw new PaymentProviderException(PaymentProviderKind.Braintree, kind, "boom");

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), Nonce));

        Assert.Equal(expected, result.Error!.Type);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task A_sibling_settling_during_the_charge_is_retried_and_the_checkout_ends_done()
    {
        // The multi-seller case: the buyer pays both orders at once. The sibling commits while this sale is in
        // flight, so this save loses on the root's version; the retry must reload the sibling as well, or the
        // checkout would never be marked done.
        var checkout = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);
        var sibling = checkout.OrderId(1);
        Psp.Braintree.OnSubmit = async request =>
        {
            await InScopeAsync(async c =>
            {
                (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId)).SucceedOrder(sibling);
                await c.SaveChangesAsync();
            });
            return new PaymentResult($"bt_{request.PaymentOrderId:N}", PaymentStatus.Succeeded);
        };

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, checkout.OrderId(0), Nonce));

        Assert.True(result.IsSuccess, result.Error?.Code);
        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.All(stored.PaymentOrders, o => Assert.Equal(PaymentOrderStatus.Success, o.Status));
        Assert.True(stored.IsPaymentDone);
        Assert.Equal(2, Events.Published.Count(e => e.Event is PaymentOrderSucceededDomainEvent));
    }

    [Fact]
    public async Task A_charge_that_lands_on_an_order_canceled_meanwhile_is_reported_not_recorded()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);
        Psp.Braintree.OnSubmit = async request =>
        {
            await InScopeAsync(async c =>
            {
                (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId)).FailOrder(id);
                await c.SaveChangesAsync();
            });
            return new PaymentResult($"bt_{request.PaymentOrderId:N}", PaymentStatus.Succeeded);
        };

        var result = await SendAsync(new SubmitPaymentMethod.Command(checkout.BuyerId, id, Nonce));

        Assert.Equal("payment_outcome_not_recorded", result.Error!.Code);
        Assert.Equal(PaymentOrderStatus.Failed, (await ReadOrderAsync(id)).Status);
        Assert.Contains(Logs.Lines, line => line.Contains("needs reconciling") && line.Contains(id.ToString()));
    }
}
