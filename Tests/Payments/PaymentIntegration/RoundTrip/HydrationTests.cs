using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;

namespace PaymentIntegration.RoundTrip;

public sealed class HydrationTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private static DateTime T0 => ControllableTimeProvider.Start.UtcDateTime;

    [Fact]
    public async Task Checkout_WithAnOrderInEveryStatus_ReloadsEveryPropertyOfRootAndChildren()
    {
        var lines = new[]
        {
            new OrderLine(Guid.NewGuid(), 10.00m, "USD"),
            new OrderLine(Guid.NewGuid(), 20.50m, "EUR"),
            new OrderLine(Guid.NewGuid(), 0.01m, "GBP"),
            new OrderLine(Guid.NewGuid(), 1234.56m, "JPY")
        };
        var saved = await SeedCheckoutAsync(lines, OrderState.NotStarted, OrderState.Executing, OrderState.Success, OrderState.Failed);

        var loaded = await ReadCheckoutAsync(saved.CheckoutId);

        Assert.Equal(saved.BuyerId, loaded.BuyerId);
        Assert.False(loaded.IsPaymentDone);
        Assert.Equal(saved.Version, loaded.Version);
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0, loaded.UpdatedAt);
        Assert.Empty(loaded.DomainEvents);
        Assert.Equal(4, loaded.PaymentOrders.Count);

        var expectedStatus = new[] { PaymentOrderStatus.NotStarted, PaymentOrderStatus.Executing, PaymentOrderStatus.Success, PaymentOrderStatus.Failed };
        for (var i = 0; i < 4; i++)
        {
            var original = saved.PaymentOrders.ElementAt(i);
            var order = loaded.Order(original.PaymentOrderId);
            Assert.Equal(expectedStatus[i], order.Status);
            Assert.Equal(saved.CheckoutId, order.CheckoutId);
            Assert.Equal(saved.BuyerId, order.BuyerId);
            Assert.Equal(lines[i].MerchantId, order.MerchantId);
            Assert.Equal(lines[i].Amount, order.Amount);
            Assert.Equal(lines[i].Currency, order.Currency);
            Assert.Equal(i == 0 ? null : CheckoutSeed.Token, order.PspToken);
            Assert.False(order.WalletUpdated);
            Assert.False(order.LedgerUpdated);
            Assert.Equal(T0, order.CreatedAt);
            Assert.Equal(T0, order.UpdatedAt);
        }
    }

    [Fact]
    public async Task Checkout_AllOrdersSucceeded_ReloadsAsPaymentDone()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success, OrderState.Success);

        Assert.True((await ReadCheckoutAsync(saved.CheckoutId)).IsPaymentDone);
    }

    [Fact]
    public async Task Checkout_OneOfTwoSucceeded_ReloadsAsNotDone()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success, OrderState.Executing);

        Assert.False((await ReadCheckoutAsync(saved.CheckoutId)).IsPaymentDone);
    }

    [Fact]
    public async Task Order_SettlementFlags_RoundTripIndependently()
    {
        var checkout = CheckoutSeed.New(orders: 2);
        var walletOnly = checkout.OrderId(0);
        var ledgerOnly = checkout.OrderId(1);
        CheckoutSeed.Drive(checkout, walletOnly, OrderState.Success);
        CheckoutSeed.Drive(checkout, ledgerOnly, OrderState.Success);
        checkout.MarkWalletUpdated(walletOnly);
        checkout.MarkLedgerUpdated(ledgerOnly);
        // Otherwise the success events run the real Settle handler on save, which sets both flags on both orders.
        checkout.ClearDomainEvents();
        Context.PaymentEvents.Add(checkout);
        await Context.SaveChangesAsync();

        var loaded = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.True(loaded.Order(walletOnly).WalletUpdated);
        Assert.False(loaded.Order(walletOnly).LedgerUpdated);
        Assert.False(loaded.Order(ledgerOnly).WalletUpdated);
        Assert.True(loaded.Order(ledgerOnly).LedgerUpdated);
    }

    [Fact]
    public async Task Order_StatusAndLedgerType_AreStoredAsTheirNames()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        Context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(checkout.PaymentOrders.Single()));
        await Context.SaveChangesAsync();

        var status = await ReadAsync(c => c.Database
            .SqlQuery<string>($"""SELECT "Status" AS "Value" FROM "PaymentOrders" """).SingleAsync());
        var types = await ReadAsync(c => c.Database
            .SqlQuery<string>($"""SELECT "Type" AS "Value" FROM "LedgerEntries" ORDER BY "Type" """).ToListAsync());

        Assert.Equal("Success", status);
        Assert.Equal(["Credit", "Debit"], types);
    }

    [Fact]
    public async Task Checkout_WithPendingDomainEvents_SavesAndReloadsWithoutThem()
    {
        var checkout = CheckoutSeed.New();
        CheckoutSeed.Drive(checkout, checkout.OrderId(0), OrderState.Success);
        Assert.NotEmpty(checkout.DomainEvents);

        Context.PaymentEvents.Add(checkout);
        await Context.SaveChangesAsync();

        Assert.Empty((await ReadCheckoutAsync(checkout.CheckoutId)).DomainEvents);
    }

    [Fact]
    public void Model_MapsExactlyTheSixTypes_AndNoDomainEventsMember()
    {
        foreach (var entityType in Context.Model.GetEntityTypes())
        {
            Assert.Null(entityType.FindProperty("DomainEvents"));
            Assert.DoesNotContain(entityType.GetNavigations(), n => n.Name == "DomainEvents");
        }

        Assert.Equal(
            [nameof(BookingClaim), nameof(LedgerEntry), nameof(OrderRefund), nameof(PaymentEvent), nameof(PaymentOrder), nameof(Wallet)],
            Context.Model.GetEntityTypes().Select(t => t.ClrType.Name).Order());
    }

    [Fact]
    public async Task Wallet_Credited_ReloadsWithBalanceAndRowVersion()
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "GBP");
        wallet.Credit(10.25m, "GBP");
        wallet.Credit(0.75m, "GBP");
        Context.Wallets.Add(wallet);
        await Context.SaveChangesAsync();

        var (loaded, version) = await ReadAsync(async c =>
        {
            var w = await c.Wallets.SingleAsync(x => x.WalletId == wallet.WalletId);
            return (w, c.Entry(w).Property<uint>("Version").CurrentValue);
        });

        Assert.Equal(wallet.OwnerId, loaded.OwnerId);
        Assert.Equal("GBP", loaded.Currency);
        Assert.Equal(11.00m, loaded.Balance);
        Assert.Equal(T0, loaded.CreatedAt);
        Assert.Equal(T0, loaded.UpdatedAt);
        Assert.NotEqual(0u, version);
    }

    [Fact]
    public async Task LedgerPair_SavedAndReloaded_IsBalancedDebitAndCredit()
    {
        var checkout = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 42.10m), OrderState.Success);
        var order = checkout.PaymentOrders.Single();
        Context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(order));
        await Context.SaveChangesAsync();

        var entries = await ReadAsync(c => c.LedgerEntries.Where(e => e.PaymentOrderId == order.PaymentOrderId).ToListAsync());

        var debit = Assert.Single(entries, e => e.Type == EntryType.Debit);
        var credit = Assert.Single(entries, e => e.Type == EntryType.Credit);
        Assert.Equal(checkout.BuyerId, debit.AccountId);
        Assert.Equal(order.MerchantId, credit.AccountId);
        Assert.All(entries, e =>
        {
            Assert.Equal(42.10m, e.Amount);
            Assert.Equal("USD", e.Currency);
            Assert.Equal(T0, e.CreatedAt);
        });
    }
}
