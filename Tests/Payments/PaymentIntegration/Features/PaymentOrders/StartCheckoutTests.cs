using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Enums;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class StartCheckoutTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_buyer_opens_the_default_psps_page_and_the_order_starts_executing_there()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(amount: 64.25m, buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.True(result.IsSuccess);
        Assert.Equal("Stripe", result.Value!.Provider);
        Assert.False(string.IsNullOrEmpty(result.Value.ClientToken));

        Assert.True(Psp.Stripe.Checkouts.TryPeek(out var request));
        Assert.Equal(new CheckoutRequest(orderId, 64.25m, "USD"), request);

        var order = (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId);
        Assert.Equal(PaymentOrderStatus.Executing, order.Status);
        Assert.Equal("Stripe", order.Provider);
        Assert.Equal(StubGateway.ReferenceFor(orderId), order.PspTokenRef);
    }

    [Fact]
    public async Task The_client_token_is_neither_stored_nor_logged()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);

        var result = await Sender.Send(new StartCheckout.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId));
        var token = result.Value!.ClientToken;

        var pattern = $"%{token}%";
        var inCheckouts = await ReadAsync(c => c.Database
            .SqlQueryRaw<int>("select count(*)::int as \"Value\" from \"PaymentEvents\" e where e::text like {0}", pattern)
            .SingleAsync());
        var inOrders = await ReadAsync(c => c.Database
            .SqlQueryRaw<int>("select count(*)::int as \"Value\" from \"PaymentOrders\" o where o::text like {0}", pattern)
            .SingleAsync());
        Assert.Equal(0, inCheckouts);
        Assert.Equal(0, inOrders);
        Assert.DoesNotContain(Logs.Lines, line => line.Contains(token));
    }

    [Fact]
    public async Task Someone_other_than_the_buyer_finds_no_order()
    {
        var checkout = await SeedCheckoutAsync();
        var order = checkout.PaymentOrders.Single();

        var result = await Sender.Send(new StartCheckout.Command(order.SellerId, order.PaymentOrderId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(Psp.Stripe.Checkouts);
    }

    [Theory]
    [InlineData(OrderState.Success)]
    [InlineData(OrderState.Failed)]
    public async Task A_settled_order_cannot_be_paid_again(OrderState state)
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(state, buyerId: buyer);

        var result = await Sender.Send(new StartCheckout.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId));

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("payment_order_settled", result.Error.Code);
        Assert.Empty(Psp.Stripe.Checkouts);
    }

    [Fact]
    public async Task A_refunded_order_cannot_be_paid_again()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Success, buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        await InScopeAsync(async c =>
        {
            (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId)).MarkOrderRefunded(orderId);
            await c.SaveChangesAsync();
        });

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.Equal("payment_order_settled", result.Error!.Code);
    }

    [Fact]
    public async Task Asking_again_goes_back_to_the_orders_own_provider_and_changes_nothing()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, buyerId: buyer, provider: "Braintree");
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.DefaultKind = PaymentProviderKind.Stripe;

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.True(result.IsSuccess);
        Assert.Equal("Braintree", result.Value!.Provider);
        Assert.Empty(Psp.Stripe.Checkouts);
        Assert.Single(Psp.Braintree.Checkouts);
        var order = (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId);
        Assert.Equal(PaymentOrderStatus.Executing, order.Status);
        Assert.Equal("Braintree", order.Provider);
        Assert.Equal(StubGateway.ReferenceFor(orderId), order.PspTokenRef);
    }

    [Theory]
    [InlineData(PaymentProviderErrorKind.Transient, ErrorType.Conflict, "psp_unavailable")]
    [InlineData(PaymentProviderErrorKind.InvalidRequest, ErrorType.BadRequest, "psp_rejected_request")]
    public async Task A_psp_error_is_answered_and_leaves_the_order_as_it_was(
        PaymentProviderErrorKind kind, ErrorType expectedType, string expectedCode)
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.Stripe.OnCheckout = _ => throw new PaymentProviderException(PaymentProviderKind.Stripe, kind, "psp error");

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.Equal(expectedType, result.Error!.Type);
        Assert.Equal(expectedCode, result.Error.Code);
        var order = (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId);
        Assert.Equal(PaymentOrderStatus.Init, order.Status);
        Assert.Null(order.Provider);
    }
}
