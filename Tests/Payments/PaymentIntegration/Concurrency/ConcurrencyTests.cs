using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;

namespace PaymentIntegration.Concurrency;

// Each race loads the same row in two scopes before either saves: the interleaving two settlement handlers
// on different instances produce.
public sealed class ConcurrencyTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private sealed class Writers : IAsyncDisposable
    {
        private readonly AsyncServiceScope _a;
        private readonly AsyncServiceScope _b;

        public Writers(AsyncServiceScope a, AsyncServiceScope b)
        {
            _a = a;
            _b = b;
        }

        public PaymentDbContext A => _a.ServiceProvider.GetRequiredService<PaymentDbContext>();
        public PaymentDbContext B => _b.ServiceProvider.GetRequiredService<PaymentDbContext>();

        public async ValueTask DisposeAsync()
        {
            await _a.DisposeAsync();
            await _b.DisposeAsync();
        }
    }

    private Writers TwoWriters() => new(NewScope(), NewScope());

    [Fact]
    public async Task Wallet_TwoConcurrentCredits_SecondIsRejectedAndOnlyFirstApplies()
    {
        await SeedWalletAsync();

        await using (var w = TwoWriters())
        {
            var wa = await w.A.Wallets.SingleAsync();
            var wb = await w.B.Wallets.SingleAsync();
            wa.Credit(30m, "USD");
            wb.Credit(70m, "USD");

            await w.A.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.B.SaveChangesAsync());
        }

        Assert.Equal(30m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }

    [Fact]
    public async Task Wallet_SequentialCreditsInSeparateScopes_BothApply()
    {
        await SeedWalletAsync();

        await InScopeAsync(async c => { (await c.Wallets.SingleAsync()).Credit(30m, "USD"); await c.SaveChangesAsync(); });
        await InScopeAsync(async c => { (await c.Wallets.SingleAsync()).Credit(70m, "USD"); await c.SaveChangesAsync(); });

        Assert.Equal(100m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }

    [Fact]
    public async Task Wallet_RejectedWriterReloadsAndRetries_BothCreditsApplyOnce()
    {
        await SeedWalletAsync();

        await using (var w = TwoWriters())
        {
            var wa = await w.A.Wallets.SingleAsync();
            var wb = await w.B.Wallets.SingleAsync();
            wa.Credit(30m, "USD");
            wb.Credit(70m, "USD");
            await w.A.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.B.SaveChangesAsync());

            await w.B.Entry(wb).ReloadAsync();
            wb.Credit(70m, "USD");
            await w.B.SaveChangesAsync();
        }

        Assert.Equal(100m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }

    [Fact]
    public async Task Order_ConcurrentSucceedAndFail_LoserIsRejectedAndOrderStaysSucceeded()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var id = saved.OrderId(0);

        Exception? loser;
        await using (var w = TwoWriters())
        {
            var a = await w.A.PaymentEvents.SingleAsync();
            var b = await w.B.PaymentEvents.SingleAsync();
            a.SucceedOrder(id);
            b.FailOrder(id);

            await w.A.SaveChangesAsync();
            loser = await Record.ExceptionAsync(() => w.B.SaveChangesAsync());
        }

        Assert.IsType<DbUpdateConcurrencyException>(loser);
        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Success, stored.Order(id).Status);
        Assert.True(stored.IsPaymentDone);
    }

    [Fact]
    public async Task Order_ConcurrentWalletUpdatedFlips_SecondIsRejected()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        var id = saved.OrderId(0);

        await using var w = TwoWriters();
        var a = await w.A.PaymentEvents.SingleAsync();
        var b = await w.B.PaymentEvents.SingleAsync();
        a.MarkWalletUpdated(id);
        b.MarkWalletUpdated(id);

        await w.A.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.B.SaveChangesAsync());
    }

    [Fact]
    public async Task TwoOrders_SucceededConcurrently_SecondRejected_RetryInFreshScopeMarksCheckoutDone()
    {
        // Without the root's token each writer sees the other order unfinished, both commit, and nobody
        // ever marks the checkout done.
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);
        var first = saved.OrderId(0);
        var second = saved.OrderId(1);

        await using (var w = TwoWriters())
        {
            var a = await w.A.PaymentEvents.SingleAsync();
            var b = await w.B.PaymentEvents.SingleAsync();
            a.SucceedOrder(first);
            b.SucceedOrder(second);

            await w.A.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.B.SaveChangesAsync());
        }

        var afterRace = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Executing, afterRace.Order(second).Status);
        Assert.False(afterRace.IsPaymentDone);

        // The redelivered callback, handled from scratch.
        await InScopeAsync(async c =>
        {
            (await c.PaymentEvents.SingleAsync()).SucceedOrder(second);
            await c.SaveChangesAsync();
        });

        var final = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.All(final.PaymentOrders, o => Assert.Equal(PaymentOrderStatus.Success, o.Status));
        Assert.True(final.IsPaymentDone);
    }

    [Fact]
    public async Task TwoOrders_LoserReloadsRootInSameContextAndRetries_CheckoutEndsDone()
    {
        // The retry most callers will write: reload the entity that failed and try again. The root's
        // decision is made over its tracked orders, so a reload that refreshes only the root leaves it
        // deciding over a stale sibling.
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);
        var first = saved.OrderId(0);
        var second = saved.OrderId(1);

        await using (var w = TwoWriters())
        {
            var a = await w.A.PaymentEvents.SingleAsync();
            var b = await w.B.PaymentEvents.SingleAsync();
            a.SucceedOrder(first);
            b.SucceedOrder(second);
            await w.A.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.B.SaveChangesAsync());

            await w.B.Entry(b).ReloadAsync();
            b.SucceedOrder(second);
            await w.B.SaveChangesAsync();
        }

        var final = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.All(final.PaymentOrders, o => Assert.Equal(PaymentOrderStatus.Success, o.Status));
        Assert.True(final.IsPaymentDone);
    }

    [Fact]
    public async Task StaleDetachedCheckout_Update_IsRefusedAndOverwritesNothing()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);
        var stale = await ReadCheckoutAsync(saved.CheckoutId);

        await InScopeAsync(async c =>
        {
            (await c.PaymentEvents.SingleAsync()).SucceedOrder(saved.OrderId(0));
            await c.SaveChangesAsync();
        });

        stale.FailOrder(saved.OrderId(0));
        Context.PaymentEvents.Update(stale);
        var error = await Record.ExceptionAsync(() => Context.SaveChangesAsync());

        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(saved.OrderId(0))).Status);
        Assert.IsType<DbUpdateConcurrencyException>(error);
    }

    [Fact]
    public async Task StaleDetachedCheckout_UpdateWithoutAnyChange_IsStillRefused()
    {
        // A blind Update of an unchanged but stale copy would write the old child rows back over the newer
        // ones; the root's token must stop that too.
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var stale = await ReadCheckoutAsync(saved.CheckoutId);

        await InScopeAsync(async c =>
        {
            (await c.PaymentEvents.SingleAsync()).SucceedOrder(saved.OrderId(0));
            await c.SaveChangesAsync();
        });

        Context.PaymentEvents.Update(stale);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Context.SaveChangesAsync());
        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(saved.OrderId(0))).Status);
    }

    [Fact]
    public async Task Settlement_TwoOrdersCreditingSameWallet_LoserRollsBackItsOrderFlagToo()
    {
        await SeedWalletAsync();
        var one = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 10m), OrderState.Success);
        var two = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 20m), OrderState.Success);

        await using (var w = TwoWriters())
        {
            var wa = await w.A.Wallets.SingleAsync();
            var ca = await w.A.PaymentEvents.SingleAsync(e => e.CheckoutId == one.CheckoutId);
            var wb = await w.B.Wallets.SingleAsync();
            var cb = await w.B.PaymentEvents.SingleAsync(e => e.CheckoutId == two.CheckoutId);
            wa.Credit(10m, "USD");
            ca.MarkWalletUpdated(one.OrderId(0));
            wb.Credit(20m, "USD");
            cb.MarkWalletUpdated(two.OrderId(0));

            await w.A.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => w.B.SaveChangesAsync());
        }

        Assert.Equal(10m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
        Assert.True((await ReadOrderAsync(one.OrderId(0))).WalletUpdated);
        Assert.False((await ReadOrderAsync(two.OrderId(0))).WalletUpdated);
    }

    [Fact]
    public async Task Settlement_SameOrderRedeliveredConcurrently_CreditsWalletOnce()
    {
        await SeedWalletAsync();
        var saved = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 15m), OrderState.Success);
        var id = saved.OrderId(0);

        await using (var w = TwoWriters())
        {
            foreach (var context in new[] { w.A, w.B })
            {
                var checkout = await context.PaymentEvents.SingleAsync();
                var wallet = await context.Wallets.SingleAsync();
                if (!checkout.Order(id).WalletUpdated)
                {
                    wallet.Credit(15m, "USD");
                    checkout.MarkWalletUpdated(id);
                }
            }

            await w.A.SaveChangesAsync();
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => w.B.SaveChangesAsync());
        }

        Assert.Equal(15m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }
}
