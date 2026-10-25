using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Messaging;

namespace PaymentIntegration.Features.PaymentOrders;

// The outcome is recorded through Wolverine, which this fixture does not run; what RecordPaymentOutcome then does is
// RecordPaymentOutcomeTests'. Here it is enough to see which stuck orders were asked about and what was handed over.
public sealed class ReconcileOrdersTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private static readonly TimeSpan PastStuck = OrderReconciler.StuckAfter + TimeSpan.FromSeconds(1);

    private async Task<int> ReconcileAsync()
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<OrderReconciler>().RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_stuck_order_the_psp_reports_paid_is_recorded_as_paid()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.Stripe.OnLookup = _ => new PaymentResult("pi_found", PaymentStatus.Succeeded);
        Clock.Advance(PastStuck);

        var recorded = await ReconcileAsync();

        Assert.Equal(1, recorded);
        Assert.Equal(new RecordPaymentOutcome(orderId, true, "pi_found"), Assert.Single(Bus.Invoked));
        Assert.True(Psp.Stripe.Lookups.TryPeek(out var lookup));
        Assert.Equal(new PaymentLookupRequest(orderId, StubGateway.ReferenceFor(orderId)), lookup);
    }

    [Fact]
    public async Task A_stuck_order_the_psp_reports_cancelled_is_recorded_as_failed()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.Stripe.OnLookup = _ => new PaymentResult("pi_found", PaymentStatus.Canceled);
        Clock.Advance(PastStuck);

        Assert.Equal(1, await ReconcileAsync());
        Assert.Equal(new RecordPaymentOutcome(orderId, false, "pi_found"), Assert.Single(Bus.Invoked));
    }

    // Not final yet: the buyer may still finish, and the expiry fails the order if they do not.
    [Theory]
    [InlineData(PaymentStatus.AwaitingPaymentMethod)]
    [InlineData(PaymentStatus.RequiresAction)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Failed)]
    public async Task A_stuck_order_the_psp_has_not_decided_is_left_alone(PaymentStatus status)
    {
        await SeedCheckoutAsync(OrderState.Executing);
        Psp.Stripe.OnLookup = _ => new PaymentResult("pi_found", status);
        Clock.Advance(PastStuck);

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Bus.Invoked);
    }

    [Fact]
    public async Task A_stuck_order_the_psp_has_no_payment_for_is_left_alone()
    {
        await SeedCheckoutAsync(OrderState.Executing);
        Psp.Stripe.OnLookup = _ => null;
        Clock.Advance(PastStuck);

        Assert.Equal(0, await ReconcileAsync());
        Assert.Single(Psp.Stripe.Lookups);
        Assert.Empty(Bus.Invoked);
    }

    // A webhook on its way normally lands first; asking sooner would only race it.
    [Fact]
    public async Task An_order_that_has_not_been_executing_long_is_not_asked_about()
    {
        await SeedCheckoutAsync(OrderState.Executing);
        Psp.Stripe.OnLookup = _ => new PaymentResult("pi_found", PaymentStatus.Succeeded);
        Clock.Advance(OrderReconciler.StuckAfter - TimeSpan.FromSeconds(1));

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Psp.Stripe.Lookups);
    }

    [Theory]
    [InlineData(OrderState.Init)]
    [InlineData(OrderState.Success)]
    [InlineData(OrderState.Failed)]
    public async Task An_order_that_is_not_executing_is_not_asked_about(OrderState state)
    {
        await SeedCheckoutAsync(state);
        Psp.Stripe.OnLookup = _ => new PaymentResult("pi_found", PaymentStatus.Succeeded);
        Clock.Advance(PastStuck);

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Psp.Stripe.Lookups);
    }

    [Fact]
    public async Task A_stuck_order_is_asked_about_at_its_own_provider()
    {
        await SeedCheckoutAsync(OrderState.Executing, provider: "Braintree");
        Psp.DefaultKind = PaymentProviderKind.Stripe;
        Psp.Braintree.OnLookup = _ => new PaymentResult("bt_found", PaymentStatus.Succeeded);
        Clock.Advance(PastStuck);

        await ReconcileAsync();

        Assert.Empty(Psp.Stripe.Lookups);
        Assert.Single(Psp.Braintree.Lookups);
    }

    [Fact]
    public async Task One_providers_error_does_not_stop_the_other_orders()
    {
        // The failing one is older, so it is asked about first.
        await SeedCheckoutAsync(OrderState.Executing, provider: "Braintree");
        Clock.Advance(TimeSpan.FromSeconds(1));
        var healthy = await SeedCheckoutAsync(OrderState.Executing, provider: "Stripe");
        var healthyOrderId = healthy.PaymentOrders.Single().PaymentOrderId;
        Psp.Braintree.OnLookup = _ => throw new PaymentProviderException(PaymentProviderKind.Braintree, PaymentProviderErrorKind.Transient, "down");
        Psp.Stripe.OnLookup = _ => new PaymentResult("pi_found", PaymentStatus.Succeeded);
        Clock.Advance(PastStuck);

        var recorded = await ReconcileAsync();

        Assert.Equal(1, recorded);
        Assert.Equal(new RecordPaymentOutcome(healthyOrderId, true, "pi_found"), Assert.Single(Bus.Invoked));
    }
}
