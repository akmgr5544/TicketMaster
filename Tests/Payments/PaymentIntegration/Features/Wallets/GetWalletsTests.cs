using PaymentIntegration.Fixtures;
using PaymentSystem.Features.Wallets;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.Wallets;

public sealed class GetWalletsTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task A_user_lists_only_their_own_wallets()
    {
        var user = Guid.NewGuid();
        var usd = await SeedWalletAsync(user, 12.34m, "USD");
        var eur = await SeedWalletAsync(user, 5m, "EUR");
        await SeedWalletAsync(Guid.NewGuid(), 99m, "USD");

        var wallets = await Sender.Send(new GetMyWallets.Query(user));

        Assert.Equal(2, wallets.Count);
        Assert.Equal(12.34m, wallets.Single(w => w.WalletId == usd.WalletId).CurrentBalance);
        Assert.Equal("EUR", wallets.Single(w => w.WalletId == eur.WalletId).Currency);
    }

    [Fact]
    public async Task The_owner_gets_one_wallet()
    {
        var user = Guid.NewGuid();
        var wallet = await SeedWalletAsync(user, 7.5m);

        var result = await Sender.Send(new GetWallet.Query(user, wallet.WalletId));

        Assert.Equal(7.5m, result.Value!.CurrentBalance);
        Assert.Equal("USD", result.Value.Currency);
    }

    [Fact]
    public async Task Someone_elses_wallet_is_not_found()
    {
        var wallet = await SeedWalletAsync(Guid.NewGuid(), 7.5m);

        var result = await Sender.Send(new GetWallet.Query(Guid.NewGuid(), wallet.WalletId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
