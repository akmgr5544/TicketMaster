using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;

namespace PaymentDomain;

public sealed class WalletTests
{
    private static Money Usd(decimal amount) => Money.Create(amount, "USD");

    [Fact]
    public void A_new_wallet_is_empty_in_its_currency()
    {
        var user = Guid.NewGuid();

        var wallet = new Wallet(user, "usd");

        Assert.Equal(user, wallet.UserId);
        Assert.Equal(Money.Zero("USD"), wallet.CurrentBalance);
    }

    [Fact]
    public void A_credit_adds_to_the_balance()
    {
        var wallet = new Wallet(Guid.NewGuid(), "USD");

        wallet.CreditFunds(Usd(10.50m));
        wallet.CreditFunds(Usd(4.25m));

        Assert.Equal(Usd(14.75m), wallet.CurrentBalance);
    }

    [Fact]
    public void A_credit_of_nothing_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => new Wallet(Guid.NewGuid(), "USD").CreditFunds(Usd(0m)));

    [Fact]
    public void A_credit_in_another_currency_is_refused()
    {
        var wallet = new Wallet(Guid.NewGuid(), "USD");

        Assert.Throws<PaymentDomainException>(() => wallet.CreditFunds(Money.Create(5m, "EUR")));
        Assert.Equal(Money.Zero("USD"), wallet.CurrentBalance);
    }

    [Fact]
    public void A_debit_takes_from_the_balance()
    {
        var wallet = new Wallet(Guid.NewGuid(), "USD");
        wallet.CreditFunds(Usd(20m));

        wallet.DebitFunds(Usd(7.50m));

        Assert.Equal(Usd(12.50m), wallet.CurrentBalance);
    }

    [Fact]
    public void A_debit_of_more_than_the_balance_is_refused_and_leaves_it_unchanged()
    {
        var wallet = new Wallet(Guid.NewGuid(), "USD");
        wallet.CreditFunds(Usd(5m));

        Assert.Throws<PaymentDomainException>(() => wallet.DebitFunds(Usd(5.01m)));
        Assert.Equal(Usd(5m), wallet.CurrentBalance);
    }

    [Fact]
    public void A_debit_of_nothing_is_refused()
    {
        var wallet = new Wallet(Guid.NewGuid(), "USD");
        wallet.CreditFunds(Usd(5m));

        Assert.Throws<PaymentDomainException>(() => wallet.DebitFunds(Usd(0m)));
    }
}
