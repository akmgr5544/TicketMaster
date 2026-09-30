using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;

namespace PaymentIntegration.Precision;

// Money columns are numeric(18,2). Anything the domain accepts must survive the round trip exactly or be
// refused by the domain; a silent rounding or a raw database overflow is a bug.
public sealed class PrecisionTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    public static TheoryData<string> Amounts => new()
    {
        "0.01", "0.001", "10.005", "10.004", "10.100", "1.999",
        "99999999999999.99", "9999999999999999.99", "99999999999999999.99",
        "79228162514264337593543950335"
    };

    [Theory]
    [MemberData(nameof(Amounts))]
    public async Task OrderAmount_IsRefusedByDomainOrRoundTripsExactly(string text)
    {
        var amount = decimal.Parse(text, CultureInfo.InvariantCulture);

        PaymentEvent checkout;
        try
        {
            checkout = PaymentEvent.Create(Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(), [new PaymentOrderLine(Guid.NewGuid(), amount, "USD")]);
        }
        catch (PaymentDomainException)
        {
            return;
        }

        Context.PaymentEvents.Add(checkout);
        Assert.Null(await Record.ExceptionAsync(() => Context.SaveChangesAsync()));
        Assert.Equal(amount, (await ReadOrderAsync(checkout.OrderId(0))).Amount);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("10.005")]
    [InlineData("99999999999999999.99")]
    public void UnstorableLineAmongValidOnes_RefusesTheWholeCheckout(string text)
    {
        var amount = decimal.Parse(text, CultureInfo.InvariantCulture);

        Assert.Throws<PaymentDomainException>(() => PaymentEvent.Create(Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(),
            [new PaymentOrderLine(Guid.NewGuid(), 10m, "USD"), new PaymentOrderLine(Guid.NewGuid(), amount, "USD")]));
    }

    [Fact]
    public async Task OrderAndLedger_LargestStorableAmount_RoundTripExactly()
    {
        var saved = await SeedCheckoutAsync(CheckoutSeed.Lines(1, 9999999999999999.99m), OrderState.Success);
        Context.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(saved.PaymentOrders.Single()));
        await Context.SaveChangesAsync();

        Assert.Equal(9999999999999999.99m, (await ReadOrderAsync(saved.OrderId(0))).Amount);
        var ledger = await ReadAsync(c => c.LedgerEntries.Select(e => e.Amount).ToListAsync());
        Assert.Equal(2, ledger.Count);
        Assert.All(ledger, a => Assert.Equal(9999999999999999.99m, a));
    }

    [Fact]
    public async Task Wallet_BalanceAtLargestStorable_RoundTripsExactly()
    {
        await SeedWalletAsync(initialCredit: 9999999999999999.99m);

        Assert.Equal(9999999999999999.99m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }

    [Fact]
    public async Task Wallet_CreditPastLimit_IsRefusedOrLandsExactly_AndStoredBalanceMatchesMemory()
    {
        await SeedWalletAsync(initialCredit: 9999999999999999.99m);

        var tracked = await Context.Wallets.SingleAsync();
        var creditError = Record.Exception(() => tracked.Credit(0.01m, "USD"));
        if (creditError is not null)
            Assert.IsType<PaymentDomainException>(creditError);
        Assert.Null(await Record.ExceptionAsync(() => Context.SaveChangesAsync()));

        Assert.Equal(tracked.Balance, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }

    [Fact]
    public async Task Wallet_SubCentCredits_AreRefusedOrAccumulateExactly()
    {
        await SeedWalletAsync();

        for (var i = 0; i < 3; i++)
        {
            var refused = false;
            await InScopeAsync(async c =>
            {
                var w = await c.Wallets.SingleAsync();
                var error = Record.Exception(() => w.Credit(0.004m, "USD"));
                if (error is not null)
                {
                    Assert.IsType<PaymentDomainException>(error);
                    refused = true;
                    return;
                }
                await c.SaveChangesAsync();
            });
            if (refused)
            {
                Assert.Equal(0m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
                return;
            }
        }

        Assert.Equal(0.012m, (await ReadAsync(c => c.Wallets.SingleAsync())).Balance);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(1000)]
    public async Task PspToken_IsRefusedByDomainOrRoundTrips(int length)
    {
        var token = new string('t', length);
        var saved = await SeedCheckoutAsync(OrderState.NotStarted);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);

        var domainError = Record.Exception(() => checkout.StartExecuting(saved.OrderId(0), token));
        if (domainError is not null)
        {
            Assert.IsType<PaymentDomainException>(domainError);
            Assert.Null((await ReadOrderAsync(saved.OrderId(0))).PspToken);
            return;
        }

        Assert.Null(await Record.ExceptionAsync(() => Context.SaveChangesAsync()));
        Assert.Equal(token, (await ReadOrderAsync(saved.OrderId(0))).PspToken);
    }
}
