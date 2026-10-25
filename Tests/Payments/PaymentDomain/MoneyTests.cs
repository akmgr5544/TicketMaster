using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;

namespace PaymentDomain;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("U5D")]
    [InlineData("")]
    public void A_currency_that_is_not_three_letters_is_refused(string currency) =>
        Assert.Throws<PaymentDomainException>(() => Money.Create(10m, currency));

    [Fact]
    public void A_missing_currency_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => Money.Create(10m, null!));

    [Fact]
    public void The_currency_is_kept_uppercased() =>
        Assert.Equal("EUR", Money.Create(10m, "eur").Currency);

    [Fact]
    public void A_negative_amount_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => Money.Create(-0.01m, "USD"));

    // numeric(18,2) would round a third decimal, and the PSP would charge a different amount than the ledger records.
    [Fact]
    public void More_than_two_decimal_places_are_refused() =>
        Assert.Throws<PaymentDomainException>(() => Money.Create(10.005m, "USD"));

    [Fact]
    public void Two_decimal_places_are_kept_exactly() =>
        Assert.Equal(10.25m, Money.Create(10.25m, "USD").Amount);

    [Fact]
    public void Zero_is_zero_in_its_currency()
    {
        var zero = Money.Zero("usd");

        Assert.Equal(0m, zero.Amount);
        Assert.Equal("USD", zero.Currency);
        Assert.True(zero.IsZero);
    }

    [Fact]
    public void Adding_amounts_in_one_currency_sums_them() =>
        Assert.Equal(Money.Create(15.75m, "USD"), Money.Create(10.50m, "USD") + Money.Create(5.25m, "USD"));

    [Fact]
    public void Adding_another_currency_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => Money.Create(10m, "USD") + Money.Create(1m, "EUR"));

    [Fact]
    public void Subtracting_amounts_in_one_currency_takes_one_from_the_other() =>
        Assert.Equal(Money.Create(4.50m, "USD"), Money.Create(10m, "USD") - Money.Create(5.50m, "USD"));

    [Fact]
    public void Subtracting_another_currency_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => Money.Create(10m, "USD") - Money.Create(1m, "EUR"));

    [Fact]
    public void Subtracting_below_zero_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => Money.Create(5m, "USD") - Money.Create(5.01m, "USD"));

    [Fact]
    public void Two_amounts_are_equal_by_value()
    {
        Assert.Equal(Money.Create(10m, "usd"), Money.Create(10.00m, "USD"));
        Assert.NotEqual(Money.Create(10m, "USD"), Money.Create(10m, "EUR"));
    }
}
