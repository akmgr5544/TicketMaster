using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;

namespace PaymentIntegration.Timestamps;

public sealed class AuditTimestampTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private static DateTime T0 => ControllableTimeProvider.Start.UtcDateTime;

    [Fact]
    public async Task Insert_EveryEntityType_StampsCreatedAndUpdatedWithClockNowAsUtc()
    {
        Clock.Advance(TimeSpan.FromMinutes(7));
        var now = Clock.GetUtcNow().UtcDateTime;

        var checkout = CheckoutSeed.New();
        CheckoutSeed.Drive(checkout, checkout.OrderId(0), OrderState.Success);
        // The ledger pair and wallet are added by hand below; the real Settle handler would add them a second time.
        checkout.ClearDomainEvents();
        Context.PaymentEvents.Add(checkout);
        Context.Wallets.Add(Wallet.Create(Guid.NewGuid(), "USD"));
        Context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(checkout.PaymentOrders.Single()));
        await Context.SaveChangesAsync();

        var (e, o, w, l) = await ReadAsync(async c => (
            await c.PaymentEvents.SingleAsync(),
            await c.Set<PaymentOrder>().SingleAsync(),
            await c.Wallets.SingleAsync(),
            await c.LedgerEntries.ToListAsync()));

        Assert.Equal(2, l.Count);
        foreach (var stamp in new[] { e.CreatedAt, e.UpdatedAt, o.CreatedAt, o.UpdatedAt, w.CreatedAt, w.UpdatedAt }
                     .Concat(l.Select(x => x.CreatedAt)))
        {
            Assert.Equal(now, stamp);
            Assert.Equal(DateTimeKind.Utc, stamp.Kind);
        }
    }

    [Fact]
    public async Task ChildOnlyChange_StampsTheChangedOrder_AndLeavesItsSiblingAlone()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);
        var changed = saved.OrderId(0);
        var sibling = saved.OrderId(1);
        Clock.Advance(TimeSpan.FromHours(1));

        (await LoadCheckoutAsync(saved.CheckoutId)).SucceedOrder(changed);
        await Context.SaveChangesAsync();

        var loaded = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(T0, loaded.Order(changed).CreatedAt);
        Assert.Equal(T0.AddHours(1), loaded.Order(changed).UpdatedAt);
        Assert.Equal(T0, loaded.Order(sibling).UpdatedAt);
        // The root's version moved, so its row was written and its UpdatedAt follows.
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0.AddHours(1), loaded.UpdatedAt);
    }

    [Fact]
    public async Task SettlementFlagChange_AdvancesOrderUpdatedAt()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        Clock.Advance(TimeSpan.FromMinutes(10));

        (await LoadCheckoutAsync(saved.CheckoutId)).MarkLedgerUpdated(saved.OrderId(0));
        await Context.SaveChangesAsync();

        var order = await ReadOrderAsync(saved.OrderId(0));
        Assert.Equal(T0, order.CreatedAt);
        Assert.Equal(T0.AddMinutes(10), order.UpdatedAt);
    }

    [Fact]
    public async Task Modify_Wallet_AdvancesUpdatedAtAndKeepsCreatedAt()
    {
        await SeedWalletAsync();
        Clock.Advance(TimeSpan.FromDays(2));

        (await Context.Wallets.SingleAsync()).Credit(5m, "USD");
        await Context.SaveChangesAsync();

        var loaded = await ReadAsync(c => c.Wallets.SingleAsync());
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0.AddDays(2), loaded.UpdatedAt);
    }

    [Fact]
    public async Task RedeliveredSucceed_WritesNothing_AndBumpsNeitherUpdatedAtNorVersion()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success, OrderState.Executing);
        var before = await ReadCheckoutAsync(saved.CheckoutId);
        Clock.Advance(TimeSpan.FromHours(5));

        var checkout = await LoadCheckoutAsync(saved.CheckoutId);
        checkout.SucceedOrder(saved.OrderId(0));
        checkout.SucceedOrder(saved.OrderId(0));
        var written = await Context.SaveChangesAsync();

        Assert.Equal(0, written);
        var after = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(T0, after.UpdatedAt);
        Assert.Equal(T0, after.Order(saved.OrderId(0)).UpdatedAt);
    }

    [Fact]
    public async Task RedeliveredFlags_WriteNothing()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        await InScopeAsync(async c =>
        {
            var root = await c.PaymentEvents.SingleAsync();
            root.MarkWalletUpdated(saved.OrderId(0));
            root.MarkLedgerUpdated(saved.OrderId(0));
            await c.SaveChangesAsync();
        });
        var version = (await ReadCheckoutAsync(saved.CheckoutId)).Version;

        var checkout = await LoadCheckoutAsync(saved.CheckoutId);
        checkout.MarkWalletUpdated(saved.OrderId(0));
        checkout.MarkLedgerUpdated(saved.OrderId(0));
        checkout.SucceedOrder(saved.OrderId(0));

        Assert.Equal(0, await Context.SaveChangesAsync());
        Assert.Equal(version, (await ReadCheckoutAsync(saved.CheckoutId)).Version);
    }

    [Fact]
    public async Task DisconnectedUpdate_FreshCheckout_SavesAndDoesNotOverwriteCreatedAt()
    {
        // A checkout loaded in one scope, changed, and attached with Update in another — nobody else has
        // written in between, so this is a legitimate save and must go through.
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var detached = await ReadCheckoutAsync(saved.CheckoutId);
        Clock.Advance(TimeSpan.FromHours(2));

        detached.SucceedOrder(saved.OrderId(0));
        Context.PaymentEvents.Update(detached);
        Context.Entry(detached).Property(e => e.CreatedAt).CurrentValue = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var error = await Record.ExceptionAsync(() => Context.SaveChangesAsync());

        Assert.Null(error);
        var loaded = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0.AddHours(2), loaded.UpdatedAt);
        Assert.Equal(T0, loaded.Order(saved.OrderId(0)).CreatedAt);
        Assert.Equal(PaymentOrderStatus.Success, loaded.Order(saved.OrderId(0)).Status);
    }

    [Fact]
    public async Task DisconnectedUpdate_Wallet_WithoutKnownVersion_IsRefusedNotBlindlyWritten()
    {
        // The row version lives in a shadow property, so a detached wallet carries none.
        await SeedWalletAsync();
        var detached = await ReadAsync(c => c.Wallets.SingleAsync());

        detached.Credit(10m, "USD");
        Context.Wallets.Update(detached);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Context.SaveChangesAsync());
        Assert.Equal(0m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }

    [Fact]
    public async Task SyncSaveChanges_StampsInsertAndUpdate()
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "USD");
        Context.Wallets.Add(wallet);
        Context.SaveChanges();

        Clock.Advance(TimeSpan.FromSeconds(90));
        wallet.Credit(1m, "USD");
        Context.SaveChanges();

        var loaded = await ReadAsync(c => c.Wallets.SingleAsync());
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0.AddSeconds(90), loaded.UpdatedAt);
    }

    [Fact]
    public async Task SyncSaveChanges_StampsCheckoutChangeWithoutEvents()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted);
        Clock.Advance(TimeSpan.FromSeconds(30));

        (await LoadCheckoutAsync(saved.CheckoutId)).StartExecuting(saved.OrderId(0), CheckoutSeed.Provider, "tok");
        Context.SaveChanges();

        var loaded = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(T0.AddSeconds(30), loaded.UpdatedAt);
        Assert.Equal(T0.AddSeconds(30), loaded.Order(saved.OrderId(0)).UpdatedAt);
        Assert.Equal(T0, loaded.Order(saved.OrderId(0)).CreatedAt);
    }
}
