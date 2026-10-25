using PaymentIntegration.Fixtures;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class GetPaymentOrderTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_buyer_and_the_seller_both_see_the_order()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 42m, buyerId: buyer);
        var order = checkout.PaymentOrders.Single();

        foreach (var caller in new[] { buyer, order.SellerId })
        {
            var result = await Sender.Send(new GetPaymentOrder.Query(caller, order.PaymentOrderId));

            Assert.True(result.IsSuccess);
            Assert.Equal(order.PaymentOrderId, result.Value!.PaymentOrderId);
            Assert.Equal(checkout.CheckoutId, result.Value.CheckoutId);
            Assert.Equal(order.SellerId, result.Value.SellerId);
            Assert.Equal(42m, result.Value.Amount);
            Assert.Equal("USD", result.Value.Currency);
            Assert.Equal("Success", result.Value.Status);
            Assert.True(result.Value.WalletUpdated);
            Assert.True(result.Value.LedgerUpdated);
        }
    }

    [Fact]
    public async Task Anyone_else_finds_no_order()
    {
        var checkout = await SeedCheckoutAsync();

        var result = await Sender.Send(new GetPaymentOrder.Query(Guid.NewGuid(), checkout.PaymentOrders.Single().PaymentOrderId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public void The_response_never_carries_the_psps_handle_on_the_payment()
    {
        var names = typeof(GetPaymentOrder.Response).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(names, name => name.Contains("Psp") || name.Contains("Token") || name.Contains("Provider"));
    }
}
