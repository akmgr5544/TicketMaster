using PaymentIntegration.Fixtures;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.Checkouts;

public sealed class GetCheckoutTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_buyer_sees_their_checkout_with_its_orders()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, amount: 75m, buyerId: buyer);
        var order = checkout.PaymentOrders.Single();

        var result = await Sender.Send(new GetCheckout.Query(buyer, checkout.BookingId));

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(checkout.CheckoutId, response.CheckoutId);
        Assert.Equal(checkout.BookingId, response.BookingId);
        Assert.Equal(buyer, response.BuyerId);
        Assert.Equal(75m, response.TotalAmount);
        Assert.Equal("USD", response.Currency);
        Assert.False(response.IsPaymentDone);
        var orderResponse = Assert.Single(response.Orders);
        Assert.Equal(order.PaymentOrderId, orderResponse.PaymentOrderId);
        Assert.Equal(order.SellerId, orderResponse.SellerId);
        Assert.Equal(75m, orderResponse.Amount);
        Assert.Equal("Executing", orderResponse.Status);
    }

    [Fact]
    public async Task Someone_elses_checkout_is_not_found()
    {
        var checkout = await SeedCheckoutAsync();

        var result = await Sender.Send(new GetCheckout.Query(Guid.NewGuid(), checkout.BookingId));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task The_seller_does_not_see_the_buyers_checkout()
    {
        var checkout = await SeedCheckoutAsync();

        var result = await Sender.Send(new GetCheckout.Query(checkout.PaymentOrders.Single().SellerId, checkout.BookingId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
