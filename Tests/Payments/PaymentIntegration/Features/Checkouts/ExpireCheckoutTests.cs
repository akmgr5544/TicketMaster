using PaymentIntegration.Fixtures;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.Checkouts;

public sealed class ExpireCheckoutTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> ExpireAsync(long bookingId) =>
        HandleAsync<CheckoutExpiryDueHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(new CheckoutExpiryDue(bookingId), context, CancellationToken.None));

    [Theory]
    [InlineData(OrderState.Init)]
    [InlineData(OrderState.Executing)]
    public async Task An_expired_unpaid_checkout_fails_its_orders_and_fails_the_payment(OrderState state)
    {
        var checkout = await SeedCheckoutAsync(state);

        var outgoing = await ExpireAsync(checkout.BookingId);

        Assert.Equal(checkout.BookingId, Assert.Single(outgoing.OfType<BookingPaymentFailedIntegrationEvent>()).BookingId);
        Assert.Equal(PaymentOrderStatus.Failed, Assert.Single((await ReadCheckoutAsync(checkout.BookingId)).PaymentOrders).Status);
    }

    [Theory]
    [InlineData(OrderState.Success, PaymentOrderStatus.Success)]
    [InlineData(OrderState.Failed, PaymentOrderStatus.Failed)]
    public async Task Expiry_leaves_a_settled_checkout_alone(OrderState state, PaymentOrderStatus expected)
    {
        var checkout = await SeedCheckoutAsync(state);

        var outgoing = await ExpireAsync(checkout.BookingId);

        Assert.Empty(outgoing);
        var stored = await ReadCheckoutAsync(checkout.BookingId);
        Assert.Equal(expected, Assert.Single(stored.PaymentOrders).Status);
        Assert.Equal(state == OrderState.Success, stored.IsPaymentDone);
    }

    [Fact]
    public async Task Expiry_for_a_checkout_that_does_not_exist_does_nothing()
    {
        var outgoing = await ExpireAsync(NewBookingId());

        Assert.Empty(outgoing);
    }
}
