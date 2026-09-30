using System.Globalization;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;

namespace PaymentDomain;

public class WalletTests
{
    private const decimal LargestStorable = 9999999999999999.99m;

    private static Wallet UsdWallet() => Wallet.Create(Guid.NewGuid(), "USD");

    [Fact]
    public void A_new_wallet_is_empty()
    {
        var ownerId = Guid.NewGuid();

        var wallet = Wallet.Create(ownerId, "USD");

        Assert.Equal(0m, wallet.Balance);
        Assert.Equal("USD", wallet.Currency);
        Assert.Equal(ownerId, wallet.OwnerId);
        Assert.NotEqual(Guid.Empty, wallet.WalletId);
        Assert.Empty(wallet.DomainEvents);
    }

    [Fact]
    public void Every_wallet_gets_its_own_id()
    {
        var ownerId = Guid.NewGuid();

        var ids = Enumerable.Range(0, 1_000).Select(_ => Wallet.Create(ownerId, "USD").WalletId).ToList();

        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Refuses_an_empty_owner() =>
        Assert.Throws<PaymentDomainException>(() => Wallet.Create(Guid.Empty, "USD"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("usd")]
    [InlineData("Usd")]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData(" USD")]
    [InlineData("US1")]
    [InlineData("ÜSD")]
    [InlineData("ＵＳＤ")]
    public void Refuses_an_invalid_currency(string? currency)
    {
        var thrown = Record.Exception(() => Wallet.Create(Guid.NewGuid(), currency!));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Fact]
    public void Credits_accumulate()
    {
        var wallet = UsdWallet();

        wallet.Credit(10.25m, "USD");
        wallet.Credit(4.75m, "USD");

        Assert.Equal(15.00m, wallet.Balance);
    }

    [Fact]
    public void Credits_accumulate_exactly_without_binary_rounding()
    {
        var wallet = UsdWallet();

        wallet.Credit(0.1m, "USD");
        wallet.Credit(0.2m, "USD");

        Assert.Equal(0.3m, wallet.Balance);
    }

    [Fact]
    public void Many_small_credits_add_up_to_the_exact_total()
    {
        var wallet = UsdWallet();

        for (var i = 0; i < 10_000; i++)
            wallet.Credit(0.01m, "USD");

        Assert.Equal(100.00m, wallet.Balance);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("usd")]
    [InlineData("Usd")]
    [InlineData("USD ")]
    [InlineData("")]
    [InlineData(null)]
    public void Refuses_a_credit_in_another_currency_and_keeps_the_balance(string? currency)
    {
        var wallet = UsdWallet();
        wallet.Credit(7.50m, "USD");

        var thrown = Record.Exception(() => wallet.Credit(10m, currency!));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(7.50m, wallet.Balance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.0")]
    [InlineData("-5")]
    [InlineData("-0.01")]
    [InlineData("-79228162514264337593543950335")]
    public void Refuses_a_non_positive_credit_and_keeps_the_balance(string amount)
    {
        var wallet = UsdWallet();
        wallet.Credit(7.50m, "USD");

        Assert.Throws<PaymentDomainException>(() => wallet.Credit(decimal.Parse(amount, CultureInfo.InvariantCulture), "USD"));
        Assert.Equal(7.50m, wallet.Balance);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("10.005")]
    [InlineData("0.0000000000000000000000000001")]
    public void Refuses_a_credit_with_more_than_two_decimal_places_and_keeps_the_balance(string amount)
    {
        var wallet = UsdWallet();
        wallet.Credit(7.50m, "USD");

        Assert.Throws<PaymentDomainException>(() => wallet.Credit(decimal.Parse(amount, CultureInfo.InvariantCulture), "USD"));
        Assert.Equal(7.50m, wallet.Balance);
    }

    [Fact]
    public void Accepts_a_credit_that_brings_the_balance_to_the_largest_storable_value()
    {
        var wallet = UsdWallet();

        wallet.Credit(LargestStorable - 1m, "USD");
        wallet.Credit(1m, "USD");

        Assert.Equal(LargestStorable, wallet.Balance);
    }

    // numeric(18,2) cannot hold the result, so the save would fail after the order was already marked paid out.
    [Fact]
    public void Refuses_a_credit_that_would_exceed_numeric_18_2_and_keeps_the_balance()
    {
        var wallet = UsdWallet();
        wallet.Credit(LargestStorable, "USD");

        var thrown = Record.Exception(() => wallet.Credit(0.01m, "USD"));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(LargestStorable, wallet.Balance);
    }

    [Fact]
    public void Refuses_a_single_credit_too_large_for_numeric_18_2()
    {
        var wallet = UsdWallet();

        var thrown = Record.Exception(() => wallet.Credit(10_000_000_000_000_000m, "USD"));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(0m, wallet.Balance);
    }

    // An OverflowException is not mapped to a 400 and escapes as a 500 from the settlement consumer.
    [Fact]
    public void A_credit_that_would_overflow_decimal_is_a_domain_error_not_an_overflow()
    {
        var wallet = UsdWallet();

        var thrown = Record.Exception(() =>
        {
            wallet.Credit(decimal.MaxValue, "USD");
            wallet.Credit(decimal.MaxValue, "USD");
        });

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.True(wallet.Balance <= LargestStorable, $"Balance {wallet.Balance} no longer fits numeric(18,2).");
    }
}
