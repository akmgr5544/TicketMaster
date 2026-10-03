using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class GetPaymentOrderTests(PaymentsFixture fixture) : QueryTest(fixture)
{
    [Fact]
    public async Task Buyer_SeesTheOrder_AsBuyer()
    {
        var buyer = Guid.NewGuid();
        var merchant = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(buyer, new OrderLine(merchant, 42.10m, "GBP"));
        await SettleAsync(checkout, checkout.OrderId(0));

        var result = await SendAsync(new GetPaymentOrder.Query(buyer, checkout.OrderId(0)));

        Assert.True(result.IsSuccess);
        var order = result.Value!;
        Assert.Equal(checkout.OrderId(0), order.PaymentOrderId);
        Assert.Equal(checkout.CheckoutId, order.CheckoutId);
        Assert.Equal(buyer, order.BuyerId);
        Assert.Equal(merchant, order.MerchantId);
        Assert.Equal(GetPaymentOrder.BuyerRole, order.ViewerRole);
        Assert.Equal(42.10m, order.Amount);
        Assert.Equal("GBP", order.Currency);
        Assert.Equal("Success", order.Status);
        Assert.True(order.WalletUpdated);
        Assert.True(order.LedgerUpdated);
    }

    [Fact]
    public async Task Merchant_SeesTheirOrder_AsMerchant()
    {
        var merchant = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(Guid.NewGuid(), new OrderLine(merchant, 7m, "USD"));

        var result = await SendAsync(new GetPaymentOrder.Query(merchant, checkout.OrderId(0)));

        Assert.True(result.IsSuccess);
        Assert.Equal(GetPaymentOrder.MerchantRole, result.Value!.ViewerRole);
        Assert.Equal("NotStarted", result.Value.Status);
    }

    [Fact]
    public async Task Merchant_OfAnotherOrderInTheSameCheckout_GetsNotFound()
    {
        // Sharing a checkout gives a seller nothing: only the order they are paid by is theirs.
        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(Guid.NewGuid(),
            new OrderLine(merchantA, 1m, "USD"),
            new OrderLine(merchantB, 2m, "USD"));
        var orderOfA = checkout.PaymentOrders.Single(o => o.MerchantId == merchantA).PaymentOrderId;

        var result = await SendAsync(new GetPaymentOrder.Query(merchantB, orderOfA));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task AnotherUser_GetsNotFound()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await SendAsync(new GetPaymentOrder.Query(Guid.NewGuid(), checkout.OrderId(0)));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task UnknownOrder_GetsTheSameNotFound()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var unknown = await SendAsync(new GetPaymentOrder.Query(checkout.BuyerId, Guid.NewGuid()));
        var foreign = await SendAsync(new GetPaymentOrder.Query(Guid.NewGuid(), checkout.OrderId(0)));

        Assert.Equal(ErrorType.NotFound, unknown.Error!.Type);
        Assert.Equal(unknown.Error.Code, foreign.Error!.Code);
    }
}
