using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentIntegration.Integrity;

public sealed class IntegrityTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task Checkout_SecondWithSameCheckoutId_IsRefusedAndFirstIsUntouched()
    {
        var first = await SeedCheckoutAsync(OrderState.NotStarted);

        Context.PaymentEvents.Add(PaymentEvent.Create(first.CheckoutId, Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(), CheckoutSeed.Lines(2)));

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        var stored = Assert.Single(await ReadAsync(c => c.PaymentEvents.ToListAsync()));
        Assert.Equal(first.BuyerId, stored.BuyerId);
        Assert.Single(stored.PaymentOrders);
        Assert.Equal(1, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
    }

    [Fact]
    public async Task Order_WhoseCheckoutWasNeverSaved_IsRefusedByForeignKey()
    {
        // Deliberately bypasses the root, to prove the database backs the aggregate boundary.
        var unsaved = CheckoutSeed.New();
        Context.Set<PaymentOrder>().Add(unsaved.PaymentOrders.Single());

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        Assert.Equal(0, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
    }

    [Fact]
    public async Task Checkout_WithOrders_CannotBeDeleted()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted, OrderState.Executing);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);

        var error = await Record.ExceptionAsync(async () =>
        {
            Context.PaymentEvents.Remove(checkout);
            await Context.SaveChangesAsync();
        });

        Assert.NotNull(error);
        Assert.Equal(1, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        Assert.Equal(2, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
    }

    [Fact]
    public async Task Checkout_WithOrders_CannotBeDeletedEvenWhenOrdersAreNotLoaded()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted);
        var bare = await Context.PaymentEvents.IgnoreAutoIncludes().SingleAsync(e => e.CheckoutId == saved.CheckoutId);

        Context.PaymentEvents.Remove(bare);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        Assert.Equal(1, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
    }

    [Fact]
    public async Task Order_WithLedgerEntries_CannotBeDeleted()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        await InScopeAsync(async c => { c.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(saved.PaymentOrders.Single())); await c.SaveChangesAsync(); });

        var order = await Context.Set<PaymentOrder>().SingleAsync();
        Context.Remove(order);

        await Assert.ThrowsAnyAsync<Exception>(() => Context.SaveChangesAsync());
        Assert.Equal(2, await ReadAsync(c => c.LedgerEntries.CountAsync()));
        Assert.Equal(1, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
    }

    [Fact]
    public async Task Checkout_TwoOrdersForTheSameSeller_AreRefused()
    {
        // "One payment order per seller": two orders for one merchant would pay the seller twice for one
        // checkout. The domain refuses it before anything reaches the database.
        var merchant = Guid.NewGuid();
        var lines = new[] { new PaymentOrderLine(merchant, 10m, "USD"), new PaymentOrderLine(merchant, 10m, "USD") };

        Assert.Throws<PaymentDomainException>(() =>
            PaymentEvent.Create(Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(), lines));
        Assert.Equal(0, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
    }

    [Fact]
    public async Task SecondOrderForTheSameSeller_WrittenAroundTheRoot_IsRefusedByTheUniqueIndex()
    {
        // Two valid checkouts for one seller, then raw SQL moves one order into the other checkout — a write
        // no aggregate would make, so only the database can refuse it.
        var merchant = Guid.NewGuid();
        var kept = await SeedCheckoutAsync([new PaymentOrderLine(merchant, 10m, "USD")], OrderState.NotStarted);
        var moved = await SeedCheckoutAsync([new PaymentOrderLine(merchant, 5m, "USD")], OrderState.NotStarted);

        var error = await Record.ExceptionAsync(() => ReadAsync(c => c.Database.ExecuteSqlAsync(
            $"""UPDATE "PaymentOrders" SET "CheckoutId" = {kept.CheckoutId} WHERE "PaymentOrderId" = {moved.OrderId(0)}""")));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error).SqlState);
        Assert.Single((await ReadCheckoutAsync(kept.CheckoutId)).PaymentOrders);
    }

    [Fact]
    public async Task LedgerPayIn_RecordedTwiceForSameOrder_SecondIsRefusedAndLedgerStaysBalanced()
    {
        var saved = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 99.99m), OrderState.Success);
        var order = saved.PaymentOrders.Single();
        Context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(order));
        await Context.SaveChangesAsync();

        // A redelivered settlement, handled by a fresh scope with no memory of the first.
        await InScopeAsync(async c =>
        {
            var reloaded = (await c.PaymentEvents.SingleAsync()).Order(order.PaymentOrderId);
            c.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(reloaded));
            await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
        });

        var entries = await ReadAsync(c => c.LedgerEntries.Where(e => e.PaymentOrderId == order.PaymentOrderId).ToListAsync());
        Assert.Single(entries, e => e.Type == EntryType.Debit);
        Assert.Single(entries, e => e.Type == EntryType.Credit);
        Assert.Equal(0m, entries.Sum(e => e.Type == EntryType.Credit ? e.Amount : -e.Amount));
    }

    [Fact]
    public async Task LedgerPayIn_RedeliveredHalfPair_IsRefused()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        var order = saved.PaymentOrders.Single();
        Context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(order));
        await Context.SaveChangesAsync();

        await InScopeAsync(async c =>
        {
            c.LedgerEntries.Add(LedgerEntry.RecordPayIn(order)[1]);
            await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
        });

        Assert.Equal(2, await ReadAsync(c => c.LedgerEntries.CountAsync()));
    }

    [Fact]
    public async Task LedgerPayIn_WithLedgerFlagInSameSave_RedeliveryRollsBackTheFlagToo()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        var id = saved.OrderId(0);
        await InScopeAsync(async c =>
        {
            var checkout = await c.PaymentEvents.SingleAsync();
            c.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(checkout.Order(id)));
            await c.SaveChangesAsync();
        });

        // Redelivery that forgot the flag was never set: entries clash, so the flag must not land alone.
        await InScopeAsync(async c =>
        {
            var checkout = await c.PaymentEvents.SingleAsync();
            c.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(checkout.Order(id)));
            checkout.MarkLedgerUpdated(id);
            await Assert.ThrowsAsync<DbUpdateException>(() => c.SaveChangesAsync());
        });

        Assert.False((await ReadOrderAsync(id)).LedgerUpdated);
        Assert.Equal(2, await ReadAsync(c => c.LedgerEntries.CountAsync()));
    }

    [Fact]
    public async Task LedgerEntry_ForUnsavedPaymentOrder_IsRefusedByForeignKey()
    {
        var unsaved = CheckoutSeed.New();
        CheckoutSeed.Drive(unsaved, unsaved.OrderId(0), OrderState.Success);
        Context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(unsaved.PaymentOrders.Single()));

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        Assert.Equal(0, await ReadAsync(c => c.LedgerEntries.CountAsync()));
    }

    [Fact]
    public async Task Wallet_SameOwnerAndCurrencyTwice_IsRefused()
    {
        var owner = Guid.NewGuid();
        await InScopeAsync(async c => { c.Wallets.Add(Wallet.Create(owner, "USD")); await c.SaveChangesAsync(); });

        Context.Wallets.Add(Wallet.Create(owner, "USD"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        Assert.Equal(1, await ReadAsync(c => c.Wallets.CountAsync()));
    }

    [Fact]
    public async Task Wallet_SameOwnerDifferentCurrency_IsAllowed()
    {
        var owner = Guid.NewGuid();
        Context.Wallets.AddRange(Wallet.Create(owner, "USD"), Wallet.Create(owner, "EUR"));
        await Context.SaveChangesAsync();

        var currencies = await ReadAsync(c => c.Wallets.Where(w => w.OwnerId == owner).Select(w => w.Currency).OrderBy(x => x).ToListAsync());
        Assert.Equal(["EUR", "USD"], currencies);
    }

    [Fact]
    public async Task FailedSave_WithMixedBatch_WritesNothing()
    {
        await SeedCheckoutAsync(OrderState.NotStarted);
        var duplicate = await ReadAsync(c => c.PaymentEvents.Select(e => e.CheckoutId).SingleAsync());

        Context.Wallets.Add(Wallet.Create(Guid.NewGuid(), "USD"));
        Context.PaymentEvents.Add(PaymentEvent.Create(duplicate, Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(), CheckoutSeed.Lines(1)));

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
        Assert.Equal(0, await ReadAsync(c => c.Wallets.CountAsync()));
    }
}
