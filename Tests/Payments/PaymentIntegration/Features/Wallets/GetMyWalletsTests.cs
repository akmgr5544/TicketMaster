using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Features.Wallets;

namespace PaymentIntegration.Features.Wallets;

public sealed class GetMyWalletsTests(PaymentsFixture fixture) : QueryTest(fixture)
{
    [Fact]
    public async Task Owner_SeesOneWalletPerCurrency_WithBalances()
    {
        var owner = Guid.NewGuid();
        var usd = Wallet.Create(owner, "USD");
        usd.Credit(120.50m, "USD");
        var eur = Wallet.Create(owner, "EUR");
        eur.Credit(0.01m, "EUR");
        var amd = Wallet.Create(owner, "AMD");
        await InScopeAsync(async c =>
        {
            c.Wallets.AddRange(usd, eur, amd);
            await c.SaveChangesAsync();
        });

        var result = await SendAsync(new GetMyWallets.Query(owner));

        Assert.True(result.IsSuccess);
        var wallets = result.Value!.Wallets;
        Assert.Equal(["AMD", "EUR", "USD"], wallets.Select(w => w.Currency));
        Assert.Equal(0m, wallets[0].Balance);
        Assert.Equal(0.01m, wallets[1].Balance);
        Assert.Equal(120.50m, wallets[2].Balance);
        Assert.Equal(usd.WalletId, wallets[2].WalletId);
    }

    [Fact]
    public async Task OtherOwnersWallets_AreNeverIncluded()
    {
        var owner = Guid.NewGuid();
        await InScopeAsync(async c =>
        {
            c.Wallets.Add(Wallet.Create(owner, "USD"));
            await c.SaveChangesAsync();
        });
        await SeedWalletAsync("USD", 500m);
        await SeedWalletAsync("EUR", 10m);

        var result = await SendAsync(new GetMyWallets.Query(owner));

        var only = Assert.Single(result.Value!.Wallets);
        Assert.Equal("USD", only.Currency);
        Assert.Equal(0m, only.Balance);
    }

    [Fact]
    public async Task NoWallets_IsAnEmptyList_NotAnError()
    {
        await SeedWalletAsync("USD", 5m);

        var result = await SendAsync(new GetMyWallets.Query(Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Wallets);
    }
}
