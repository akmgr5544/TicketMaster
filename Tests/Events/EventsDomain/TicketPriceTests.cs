using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;

namespace EventsDomain;

public class TicketPriceTests
{
    [Fact]
    public void Normalises_the_currency_to_upper_case()
    {
        Assert.Equal("EUR", new TicketPrice(10m, "eur").Currency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_price_that_is_not_positive(decimal amount)
    {
        Assert.Throws<EventsDomainException>(() => new TicketPrice(amount, "USD"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("U$D")]
    [InlineData(null)]
    public void Rejects_a_currency_that_is_not_three_letters(string? currency)
    {
        Assert.Throws<EventsDomainException>(() => new TicketPrice(10m, currency!));
    }
}
