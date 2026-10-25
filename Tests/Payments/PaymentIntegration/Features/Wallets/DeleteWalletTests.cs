using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Features.Wallets;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.Wallets;

public sealed class DeleteWalletTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task An_empty_wallet_is_removed()
    {
        var user = Guid.NewGuid();
        var wallet = await SeedWalletAsync(user);

        var result = await Sender.Send(new DeleteWallet.Command(user, wallet.WalletId));

        Assert.True(result.IsSuccess);
        Assert.False(await ReadAsync(c => c.Wallets.AnyAsync(w => w.WalletId == wallet.WalletId)));
    }

    [Fact]
    public async Task A_wallet_holding_money_is_kept()
    {
        var user = Guid.NewGuid();
        var wallet = await SeedWalletAsync(user, balance: 0.01m);

        var result = await Sender.Send(new DeleteWallet.Command(user, wallet.WalletId));

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("wallet_not_empty", result.Error.Code);
        Assert.True(await ReadAsync(c => c.Wallets.AnyAsync(w => w.WalletId == wallet.WalletId)));
    }

    [Fact]
    public async Task Someone_elses_wallet_is_not_found_and_stays()
    {
        var wallet = await SeedWalletAsync(Guid.NewGuid());

        var result = await Sender.Send(new DeleteWallet.Command(Guid.NewGuid(), wallet.WalletId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.True(await ReadAsync(c => c.Wallets.AnyAsync(w => w.WalletId == wallet.WalletId)));
    }
}
