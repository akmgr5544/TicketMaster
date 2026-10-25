using PaymentIntegration.Fixtures;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.RefundOrders;

public sealed class GetOrderRefundsTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_buyer_and_the_seller_see_the_orders_refunds_oldest_first()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Success, buyerId: buyer);
        var order = checkout.PaymentOrders.Single();
        var older = await SeedRefundAsync(order.PaymentOrderId, 10m);
        Clock.Advance(TimeSpan.FromMinutes(5));
        var newer = await SeedRefundAsync(order.PaymentOrderId, 20m);

        foreach (var caller in new[] { buyer, order.SellerId })
        {
            var result = await Sender.Send(new GetOrderRefunds.Query(caller, order.PaymentOrderId));

            Assert.True(result.IsSuccess);
            Assert.Equal([older.RefundOrderId, newer.RefundOrderId], result.Value!.Select(r => r.RefundOrderId));
            Assert.Equal([10m, 20m], result.Value!.Select(r => r.RefundAmount));
        }
    }

    [Fact]
    public async Task Anyone_else_finds_no_order()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var order = checkout.PaymentOrders.Single();
        await SeedRefundAsync(order.PaymentOrderId, 10m);

        var result = await Sender.Send(new GetOrderRefunds.Query(Guid.NewGuid(), order.PaymentOrderId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
