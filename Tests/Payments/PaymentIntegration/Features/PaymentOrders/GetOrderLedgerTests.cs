using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class GetOrderLedgerTests(PaymentsFixture fixture) : QueryTest(fixture)
{
    [Fact]
    public async Task Buyer_SeesTheBalancedPair()
    {
        var buyer = Guid.NewGuid();
        var merchant = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(buyer, new OrderLine(merchant, 250.75m, "USD"));
        await SettleAsync(checkout, checkout.OrderId(0));

        var result = await SendAsync(new GetOrderLedger.Query(buyer, checkout.OrderId(0)));

        Assert.True(result.IsSuccess);
        var ledger = result.Value!;
        Assert.Equal(checkout.OrderId(0), ledger.PaymentOrderId);
        Assert.Equal(0m, ledger.SignedSum);
        Assert.Equal(2, ledger.Entries.Count);

        var debit = ledger.Entries.Single(e => e.Type == "Debit");
        Assert.Equal(buyer, debit.AccountId);
        Assert.Equal(250.75m, debit.Amount);
        Assert.Equal("USD", debit.Currency);

        var credit = ledger.Entries.Single(e => e.Type == "Credit");
        Assert.Equal(merchant, credit.AccountId);
        Assert.Equal(250.75m, credit.Amount);
    }

    [Fact]
    public async Task Merchant_SeesTheSameLedger()
    {
        var merchant = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(Guid.NewGuid(), new OrderLine(merchant, 9.99m, "EUR"));
        await SettleAsync(checkout, checkout.OrderId(0));

        var result = await SendAsync(new GetOrderLedger.Query(merchant, checkout.OrderId(0)));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Entries.Count);
        Assert.Equal(0m, result.Value.SignedSum);
    }

    [Fact]
    public async Task VisibleOrderNotYetSettled_IsAnEmptyLedger_NotNotFound()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await SendAsync(new GetOrderLedger.Query(checkout.BuyerId, checkout.OrderId(0)));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Entries);
        Assert.Equal(0m, result.Value.SignedSum);
    }

    [Fact]
    public async Task OnlyThisOrdersEntries_AreReturned()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(buyer, CheckoutSeed.Lines(2, 10m));
        await SettleAsync(checkout, checkout.OrderId(0));
        await SettleAsync(checkout, checkout.OrderId(1));

        var result = await SendAsync(new GetOrderLedger.Query(buyer, checkout.OrderId(0)));

        Assert.Equal(2, result.Value!.Entries.Count);
        var credit = result.Value.Entries.Single(e => e.Type == "Credit");
        Assert.Equal(checkout.Order(checkout.OrderId(0)).MerchantId, credit.AccountId);
    }

    [Fact]
    public async Task AnotherUser_GetsNotFound_EvenWhenEntriesExist()
    {
        var checkout = await SeedCheckoutForAsync(Guid.NewGuid(), CheckoutSeed.Lines(1));
        await SettleAsync(checkout, checkout.OrderId(0));

        var result = await SendAsync(new GetOrderLedger.Query(Guid.NewGuid(), checkout.OrderId(0)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task UnknownOrder_GetsTheSameNotFound()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var unknown = await SendAsync(new GetOrderLedger.Query(checkout.BuyerId, Guid.NewGuid()));
        var foreign = await SendAsync(new GetOrderLedger.Query(Guid.NewGuid(), checkout.OrderId(0)));

        Assert.Equal(ErrorType.NotFound, unknown.Error!.Type);
        Assert.Equal(unknown.Error.Code, foreign.Error!.Code);
    }
}
