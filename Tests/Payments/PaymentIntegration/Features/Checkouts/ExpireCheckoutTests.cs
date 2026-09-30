using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentProvider.Models;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentIntegration.Features.Checkouts;

public sealed class ExpireCheckoutTests : PspTest
{
    private readonly PaymentsFixture _fixture;

    public ExpireCheckoutTests(PaymentsFixture fixture) : base(fixture)
    {
        _fixture = fixture;
        Outbox.Reset();
    }

    private IntegrationEventLog Outbox => _fixture.Services.GetRequiredService<IntegrationEventLog>();

    // Through the Wolverine consumer, the way the scheduled message is delivered.
    private async Task FireAsync(Guid checkoutId)
    {
        await using var scope = NewScope();
        await new CheckoutExpiryDueConsumer(scope.ServiceProvider.GetRequiredService<ISender>())
            .Consume(new CheckoutExpiryDue(checkoutId), CancellationToken.None);
    }

    [Fact]
    public async Task Firing_fails_the_unpaid_order_and_tells_Bookings_once_inside_the_transaction()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        await FireAsync(checkout.CheckoutId);

        Assert.Equal(PaymentOrderStatus.Failed, (await OrderAsync(checkout, 0)).Status);
        var published = Assert.Single(Outbox.Published);
        Assert.Equal(new BookingPaymentFailedIntegrationEvent(checkout.BookingId), published.Event);
        Assert.NotNull(published.TransactionId);
    }

    [Fact]
    public async Task Firing_fails_orders_never_started_as_well_as_those_executing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.Executing);

        await FireAsync(checkout.CheckoutId);

        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.All(stored.PaymentOrders, o => Assert.Equal(PaymentOrderStatus.Failed, o.Status));
        Assert.False(stored.IsPaymentDone);
        Assert.All(Outbox.OfType<BookingPaymentFailedIntegrationEvent>(), m => Assert.Equal(checkout.BookingId, m.BookingId));
        Assert.NotEmpty(Outbox.OfType<BookingPaymentFailedIntegrationEvent>());
    }

    [Fact]
    public async Task Firing_again_changes_nothing_and_publishes_nothing_more()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        await FireAsync(checkout.CheckoutId);
        var version = (await ReadCheckoutAsync(checkout.CheckoutId)).Version;

        await FireAsync(checkout.CheckoutId);

        Assert.Single(Outbox.Published);
        Assert.Equal(version, (await ReadCheckoutAsync(checkout.CheckoutId)).Version);
    }

    [Fact]
    public async Task Firing_on_a_paid_checkout_is_a_no_op()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var before = await ReadCheckoutAsync(checkout.CheckoutId);

        await FireAsync(checkout.CheckoutId);

        var after = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Success, after.Order(checkout.OrderId(0)).Status);
        Assert.True(after.IsPaymentDone);
        Assert.Equal(before.Version, after.Version);
        Assert.Empty(Outbox.Published);
        Assert.DoesNotContain(Logs.Lines, line => line.StartsWith("Warning"));
    }

    [Fact]
    public async Task Firing_on_a_partly_paid_checkout_fails_the_rest_keeps_the_paid_order_and_flags_it()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, OrderState.Executing);

        await FireAsync(checkout.CheckoutId);

        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Success, stored.Order(checkout.OrderId(0)).Status);
        Assert.Equal(PaymentOrderStatus.Failed, stored.Order(checkout.OrderId(1)).Status);
        Assert.Equal(checkout.BookingId, Assert.Single(Outbox.OfType<BookingPaymentFailedIntegrationEvent>()).BookingId);
        Assert.Contains(Logs.Lines, line => line.StartsWith("Warning") && line.Contains("reconciling"));
    }

    [Fact]
    public async Task A_success_the_provider_reports_after_expiry_is_refused_and_flagged_for_reconciling()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        await FireAsync(checkout.CheckoutId);
        Events.Reset();
        Outbox.Reset();

        var result = await WebhookAsync(checkout.OrderId(0), PaymentStatus.Succeeded);

        // Acknowledged: a redelivery would be refused the same way.
        Assert.Equal("unchanged", result.Value!.Outcome);
        Assert.Equal(PaymentOrderStatus.Failed, (await OrderAsync(checkout, 0)).Status);
        Assert.Empty(Events.Published);
        Assert.Empty(Outbox.Published);
        Assert.Contains(Logs.Lines, line => line.StartsWith("Warning") && line.Contains("needs reconciling"));
    }

    [Fact]
    public async Task Firing_for_a_checkout_that_does_not_exist_is_a_no_op()
    {
        await FireAsync(Guid.NewGuid());

        Assert.Empty(Outbox.Published);
    }
}
