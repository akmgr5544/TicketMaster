using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Domain.Events;
using PaymentSystem.Enums;
using PaymentSystem.Features.PaymentOrders;

namespace PaymentIntegration.Features.PaymentOrders;

// An order the service lost track of — charged, but the outcome never recorded — is asked about again, so it
// settles before the checkout's expiry fails it over money already taken.
public sealed class ReconcileOrdersTests(PaymentsFixture fixture) : PspTest(fixture)
{
    private static readonly TimeSpan Stale = ReconcileOrdersJob.StaleAfter + TimeSpan.FromSeconds(1);

    private async Task ReconcileAsync() =>
        await fixture.Services.GetServices<IHostedService>().OfType<ReconcileOrdersJob>().Single()
            .RunOnceAsync(CancellationToken.None);

    private void ProviderReports(PaymentStatus status) =>
        Psp.Stripe.OnLookup = request => new PaymentResult(StubGateway.ReferenceFor(request.PaymentOrderId), status);

    [Fact]
    public async Task A_stale_order_the_provider_reports_succeeded_is_settled()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        Clock.Advance(Stale);
        ProviderReports(PaymentStatus.Succeeded);

        await ReconcileAsync();

        var stored = await OrderAsync(checkout, 0);
        Assert.Equal(PaymentOrderStatus.Success, stored.Status);
        Assert.True(stored is { WalletUpdated: true, LedgerUpdated: true });
        Assert.Single(Events.Published, e => e.Event is PaymentOrderSucceededDomainEvent);
    }

    [Fact]
    public async Task A_stale_order_the_provider_reports_canceled_is_failed()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        Clock.Advance(Stale);
        ProviderReports(PaymentStatus.Canceled);

        await ReconcileAsync();

        Assert.Equal(PaymentOrderStatus.Failed, (await OrderAsync(checkout, 0)).Status);
        Assert.Single(Events.Published, e => e.Event is PaymentOrderFailedDomainEvent);
    }

    // The buyer may still be on the provider's page; only a final answer moves the order.
    [Theory]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.AwaitingPaymentMethod)]
    [InlineData(PaymentStatus.Failed)]
    public async Task A_stale_order_the_provider_has_not_settled_is_left_executing(PaymentStatus status)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        Clock.Advance(Stale);
        ProviderReports(status);

        await ReconcileAsync();

        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task A_stale_order_the_provider_has_no_payment_for_is_left_executing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        Clock.Advance(Stale);

        await ReconcileAsync();

        Assert.Single(Psp.Stripe.Lookups);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task The_lookup_names_the_order_and_its_reference_at_the_provider_that_started_it()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        Clock.Advance(Stale);
        Psp.DefaultKind = PaymentProviderKind.Braintree;

        await ReconcileAsync();

        Assert.Equal(new PaymentLookupRequest(checkout.OrderId(0), CheckoutSeed.Token), Assert.Single(Psp.Stripe.Lookups));
        Assert.Empty(Psp.Braintree.Lookups);
    }

    [Fact]
    public async Task An_order_started_recently_is_not_asked_about()
    {
        await SeedCheckoutAsync(OrderState.Executing);
        Clock.Advance(ReconcileOrdersJob.StaleAfter - TimeSpan.FromSeconds(1));

        await ReconcileAsync();

        Assert.Empty(Psp.Stripe.Lookups);
    }

    [Theory]
    [InlineData(OrderState.NotStarted)]
    [InlineData(OrderState.Success)]
    [InlineData(OrderState.Failed)]
    public async Task An_order_that_is_not_executing_is_not_asked_about(OrderState state)
    {
        await SeedCheckoutAsync(state);
        Clock.Advance(Stale);

        await ReconcileAsync();

        Assert.Empty(Psp.Stripe.Lookups);
    }

    [Fact]
    public async Task One_order_the_provider_errors_on_does_not_stop_the_others()
    {
        var failing = await SeedCheckoutAsync(OrderState.Executing);
        var healthy = await SeedCheckoutAsync(OrderState.Executing);
        Clock.Advance(Stale);
        Psp.Stripe.OnLookup = request => request.PaymentOrderId == failing.OrderId(0)
            ? throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Transient, "down")
            : new PaymentResult(StubGateway.ReferenceFor(request.PaymentOrderId), PaymentStatus.Succeeded);

        await ReconcileAsync();

        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(failing, 0)).Status);
        Assert.Equal(PaymentOrderStatus.Success, (await OrderAsync(healthy, 0)).Status);
    }
}
