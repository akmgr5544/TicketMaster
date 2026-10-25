using PaymentIntegration.Fixtures;
using PaymentProvider.Models;
using PaymentSystem.Enums;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Messaging;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

// RecordPaymentOutcome is invoked through Wolverine, which this fixture does not run; what it does with the outcome
// is RecordPaymentOutcomeTests'. Here it is enough that the right outcome was handed over, and only a final one.
public sealed class SubmitPaymentMethodTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_payment_method_goes_to_the_orders_own_provider_with_the_amount()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, amount: 33.10m, buyerId: buyer, provider: "Braintree");
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        await Sender.Send(new SubmitPaymentMethod.Command(buyer, orderId, "nonce-abc"));

        Assert.Empty(Psp.Stripe.Submissions);
        Assert.True(Psp.Braintree.Submissions.TryPeek(out var request));
        Assert.Equal(new SubmitPaymentMethodRequest(orderId, 33.10m, "USD", "nonce-abc"), request);
    }

    [Fact]
    public async Task A_braintree_charge_that_succeeds_is_recorded_as_paid()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, buyerId: buyer, provider: "Braintree");
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        var result = await Sender.Send(new SubmitPaymentMethod.Command(buyer, orderId, "nonce"));

        Assert.Equal("Succeeded", result.Value!.Status);
        var outcome = Assert.IsType<RecordPaymentOutcome>(Assert.Single(Bus.Invoked));
        Assert.Equal(new RecordPaymentOutcome(orderId, true, $"bt_{orderId:N}"), outcome);
    }

    [Fact]
    public async Task A_braintree_charge_that_is_cancelled_is_recorded_as_failed()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, buyerId: buyer, provider: "Braintree");
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.Braintree.OnSubmit = _ => Task.FromResult<PaymentResult?>(new PaymentResult("bt_x", PaymentStatus.Canceled));

        await Sender.Send(new SubmitPaymentMethod.Command(buyer, orderId, "nonce"));

        var outcome = Assert.IsType<RecordPaymentOutcome>(Assert.Single(Bus.Invoked));
        Assert.Equal(new RecordPaymentOutcome(orderId, false, "bt_x"), outcome);
    }

    [Fact]
    public async Task A_declined_attempt_is_not_final_so_nothing_is_recorded()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, buyerId: buyer, provider: "Braintree");
        Psp.Braintree.OnSubmit = _ =>
            Task.FromResult<PaymentResult?>(new PaymentResult("bt_x", PaymentStatus.Failed, "insufficient funds"));

        var result = await Sender.Send(new SubmitPaymentMethod.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId, "nonce"));

        Assert.Equal("Failed", result.Value!.Status);
        Assert.Equal("insufficient funds", result.Value.FailureReason);
        Assert.Empty(Bus.Invoked);
    }

    [Fact]
    public async Task With_stripe_the_answer_comes_by_webhook_so_the_submission_is_pending()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, buyerId: buyer, provider: "Stripe");

        var result = await Sender.Send(new SubmitPaymentMethod.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId, "pm"));

        Assert.Equal("Pending", result.Value!.Status);
        Assert.Empty(Bus.Invoked);
    }

    [Fact]
    public async Task Someone_other_than_the_buyer_finds_no_order()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing, provider: "Braintree");
        var order = checkout.PaymentOrders.Single();

        var result = await Sender.Send(new SubmitPaymentMethod.Command(order.SellerId, order.PaymentOrderId, "nonce"));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(Psp.Braintree.Submissions);
    }

    [Theory]
    [InlineData(OrderState.Init)]
    [InlineData(OrderState.Success)]
    [InlineData(OrderState.Failed)]
    public async Task An_order_that_is_not_executing_takes_no_payment_method(OrderState state)
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(state, buyerId: buyer, provider: "Braintree");

        var result = await Sender.Send(new SubmitPaymentMethod.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId, "nonce"));

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Empty(Psp.Braintree.Submissions);
        Assert.Empty(Psp.Stripe.Submissions);
    }

    [Fact]
    public async Task The_order_itself_is_not_changed_by_the_submission()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, buyerId: buyer, provider: "Braintree");
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        await Sender.Send(new SubmitPaymentMethod.Command(buyer, orderId, "nonce"));

        Assert.Equal(PaymentOrderStatus.Executing, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId).Status);
    }
}
