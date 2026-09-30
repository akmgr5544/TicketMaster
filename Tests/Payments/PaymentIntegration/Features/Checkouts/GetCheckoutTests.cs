using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.Checkouts;

public sealed class GetCheckoutTests(PaymentsFixture fixture) : QueryTest(fixture)
{
    [Fact]
    public async Task Buyer_SeesTheirCheckoutWithEveryOrder()
    {
        var buyer = Guid.NewGuid();
        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(buyer,
            new PaymentOrderLine(merchantA, 10.25m, "USD"),
            new PaymentOrderLine(merchantB, 99.99m, "EUR"));
        await SettleAsync(checkout, checkout.OrderId(0));

        var result = await SendAsync(new GetCheckout.Query(buyer, checkout.BookingId));

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(checkout.CheckoutId, response.CheckoutId);
        Assert.Equal(checkout.BookingId, response.BookingId);
        Assert.False(response.IsPaymentDone);
        Assert.Equal(ControllableTimeProvider.Start.UtcDateTime, response.CreatedAt);
        Assert.Equal(2, response.Orders.Count);

        var settled = response.Orders.Single(o => o.PaymentOrderId == checkout.OrderId(0));
        Assert.Equal(merchantA, settled.MerchantId);
        Assert.Equal(10.25m, settled.Amount);
        Assert.Equal("USD", settled.Currency);
        Assert.Equal("Success", settled.Status);
        Assert.True(settled.WalletUpdated);
        Assert.True(settled.LedgerUpdated);

        var pending = response.Orders.Single(o => o.PaymentOrderId == checkout.OrderId(1));
        Assert.Equal(merchantB, pending.MerchantId);
        Assert.Equal(99.99m, pending.Amount);
        Assert.Equal("EUR", pending.Currency);
        Assert.Equal("NotStarted", pending.Status);
        Assert.False(pending.WalletUpdated);
        Assert.False(pending.LedgerUpdated);
    }

    [Fact]
    public async Task EveryOrderSettled_ReportsPaymentDone()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(buyer, CheckoutSeed.Lines(2));
        await SettleAsync(checkout, checkout.OrderId(0));
        await SettleAsync(checkout, checkout.OrderId(1));

        var result = await SendAsync(new GetCheckout.Query(buyer, checkout.BookingId));

        Assert.True(result.Value!.IsPaymentDone);
        Assert.All(result.Value.Orders, o => Assert.Equal("Success", o.Status));
    }

    [Fact]
    public async Task AnotherUser_GetsNotFound()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await SendAsync(new GetCheckout.Query(Guid.NewGuid(), checkout.BookingId));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task TheMerchant_GetsNotFound()
    {
        // A checkout is the buyer's view of a purchase; sellers see their own order, not the checkout.
        var merchant = Guid.NewGuid();
        var checkout = await SeedCheckoutForAsync(Guid.NewGuid(), new PaymentOrderLine(merchant, 5m, "USD"));

        var result = await SendAsync(new GetCheckout.Query(merchant, checkout.BookingId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task UnknownBooking_GetsNotFound_SameAsSomeoneElses()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var unknown = await SendAsync(new GetCheckout.Query(checkout.BuyerId, checkout.BookingId + 1));
        var foreign = await SendAsync(new GetCheckout.Query(Guid.NewGuid(), checkout.BookingId));

        Assert.Equal(ErrorType.NotFound, unknown.Error!.Type);
        Assert.Equal(ErrorType.NotFound, foreign.Error!.Type);
        Assert.Equal(unknown.Error.Code, foreign.Error.Code);
    }
}
