using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;

namespace PaymentIntegration.RoundTrip;

// The root decides "every order succeeded" over the orders it holds, so any load path that hands it a
// partial collection can mark a checkout done while an order is still unpaid.
public sealed class AggregateLoadingTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task Query_InFreshScope_BringsEveryOrder()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.Executing, OrderState.Success);

        var loaded = await ReadCheckoutAsync(saved.CheckoutId);

        Assert.Equal(saved.PaymentOrders.Select(o => o.PaymentOrderId).Order(), loaded.PaymentOrders.Select(o => o.PaymentOrderId).Order());
    }

    [Fact]
    public async Task FindAsync_InFreshScope_BringsEveryOrder()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.Executing);

        var loaded = await ReadAsync(async c => await c.PaymentEvents.FindAsync(saved.CheckoutId));

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.PaymentOrders.Count);
    }

    [Fact]
    public async Task AsNoTrackingQuery_BringsEveryOrder()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.Executing);

        var loaded = await ReadAsync(c => c.PaymentEvents.AsNoTracking().SingleAsync(e => e.CheckoutId == saved.CheckoutId));

        Assert.Equal(2, loaded.PaymentOrders.Count);
    }

    [Fact]
    public async Task ListQuery_OverSeveralCheckouts_GivesEachOnlyItsOwnOrders()
    {
        var a = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.NotStarted);
        var b = await SeedCheckoutAsync(OrderState.NotStarted);

        var loaded = await ReadAsync(c => c.PaymentEvents.ToListAsync());

        Assert.Equal(2, loaded.Single(x => x.CheckoutId == a.CheckoutId).PaymentOrders.Count);
        Assert.Single(loaded.Single(x => x.CheckoutId == b.CheckoutId).PaymentOrders);
        Assert.All(loaded, x => Assert.All(x.PaymentOrders, o => Assert.Equal(x.CheckoutId, o.CheckoutId)));
    }

    [Fact]
    public async Task PartiallyLoadedCheckout_SucceedingTheLoadedOrder_NeverMarksCheckoutDone()
    {
        // IgnoreAutoIncludes + a filtered Include hands the root one of its two orders. Whatever the load
        // path, the stored checkout must not claim "done" while the other order is still NotStarted.
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.NotStarted);
        var target = saved.OrderId(0);

        var error = await Record.ExceptionAsync(async () =>
        {
            var partial = await Context.PaymentEvents
                .IgnoreAutoIncludes()
                .Include(e => e.PaymentOrders.Where(o => o.PaymentOrderId == target))
                .SingleAsync(e => e.CheckoutId == saved.CheckoutId);
            partial.SucceedOrder(target);
            await Context.SaveChangesAsync();
        });

        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.False(stored.IsPaymentDone,
            $"Checkout marked done with an order still {stored.Order(saved.OrderId(1)).Status} (load error: {error?.GetType().Name ?? "none"}).");
    }

    [Fact]
    public async Task RootWithoutOrdersLoaded_CannotSettleAnything()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);

        var bare = await Context.PaymentEvents.IgnoreAutoIncludes().SingleAsync(e => e.CheckoutId == saved.CheckoutId);

        Assert.Throws<PaymentSystem.Domain.Exceptions.PaymentDomainException>(() => bare.SucceedOrder(saved.OrderId(0)));
        Assert.Equal(PaymentOrderStatus.Executing, (await ReadOrderAsync(saved.OrderId(0))).Status);
    }

    [Fact]
    public async Task Orders_CannotBeRemovedThroughTheRootsCollection()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.NotStarted);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);

        if (checkout.PaymentOrders is ICollection<PaymentOrder> collection)
            Assert.ThrowsAny<NotSupportedException>(() => collection.Remove(collection.First()));
        Assert.Null(typeof(PaymentEvent).GetMethod("RemoveOrder"));
        await Context.SaveChangesAsync();

        Assert.Equal(2, (await ReadCheckoutAsync(saved.CheckoutId)).PaymentOrders.Count);
    }
}
