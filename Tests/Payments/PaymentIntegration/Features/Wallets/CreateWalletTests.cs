using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Features.Wallets;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.Wallets;

public sealed class CreateWalletTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task A_new_wallet_is_empty_and_kept_in_its_currency()
    {
        var user = Guid.NewGuid();

        var result = await Sender.Send(new CreateWallet.Command(user, "eur"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.CurrentBalance);
        Assert.Equal("EUR", result.Value.Currency);
        var stored = await ReadAsync(c => c.Wallets.SingleAsync(w => w.UserId == user));
        Assert.Equal(0m, stored.CurrentBalance.Amount);
        Assert.Equal("EUR", stored.CurrentBalance.Currency);
    }

    [Fact]
    public async Task A_second_wallet_in_the_same_currency_is_refused()
    {
        var user = Guid.NewGuid();
        await SeedWalletAsync(user, currency: "EUR");

        var result = await Sender.Send(new CreateWallet.Command(user, "eur"));

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("wallet_exists", result.Error.Code);
        Assert.Equal(1, await ReadAsync(c => c.Wallets.CountAsync(w => w.UserId == user)));
    }

    [Fact]
    public async Task Wallets_in_different_currencies_live_side_by_side()
    {
        var user = Guid.NewGuid();
        await SeedWalletAsync(user, currency: "EUR");

        var result = await Sender.Send(new CreateWallet.Command(user, "USD"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await ReadAsync(c => c.Wallets.CountAsync(w => w.UserId == user)));
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public async Task A_currency_that_is_not_an_iso_code_is_refused(string currency)
    {
        await Assert.ThrowsAsync<PaymentDomainException>(() => Sender.Send(new CreateWallet.Command(Guid.NewGuid(), currency)));
    }

    [Fact]
    public async Task The_database_refuses_a_second_wallet_in_one_currency()
    {
        var user = Guid.NewGuid();
        await SeedWalletAsync(user, currency: "EUR");

        await Assert.ThrowsAsync<DbUpdateException>(() => SeedWalletAsync(user, currency: "EUR"));
    }
}
