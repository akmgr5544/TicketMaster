using PaymentSystem.Enums;

namespace PaymentDomain;

// Providers deliver at least once and out of order, so a repeat, a stale answer and an early one all arrive in
// normal operation: the checkout reports each rather than throwing.
public class ProviderAnswerTests
{
    [Theory]
    [InlineData(PaymentOrderStatus.NotStarted, true, OrderUpdate.NotStarted, PaymentOrderStatus.NotStarted)]
    [InlineData(PaymentOrderStatus.NotStarted, false, OrderUpdate.NotStarted, PaymentOrderStatus.NotStarted)]
    [InlineData(PaymentOrderStatus.Executing, true, OrderUpdate.Applied, PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Executing, false, OrderUpdate.Applied, PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Success, true, OrderUpdate.AlreadyApplied, PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Success, false, OrderUpdate.Superseded, PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Failed, false, OrderUpdate.AlreadyApplied, PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Failed, true, OrderUpdate.Superseded, PaymentOrderStatus.Failed)]
    public void Reports_what_the_answer_did_to_the_order(PaymentOrderStatus from, bool succeeded, OrderUpdate expected,
        PaymentOrderStatus after)
    {
        var checkout = Checkouts.SingleOrderIn(from);

        var update = checkout.ApplyProviderAnswer(checkout.OnlyId(), succeeded);

        Assert.Equal(expected, update);
        Assert.Equal(after, checkout.Only().Status);
    }

    // The payment stays succeeded at the provider after a refund, so a late success is old news.
    [Theory]
    [InlineData(true, OrderUpdate.AlreadyApplied)]
    [InlineData(false, OrderUpdate.Superseded)]
    public void A_refunded_order_is_left_refunded(bool succeeded, OrderUpdate expected)
    {
        var checkout = Checkouts.SingleOrder();
        checkout.Settle(checkout.OnlyId());
        checkout.RefundOrder(checkout.OnlyId(), "re_1");

        Assert.Equal(expected, checkout.ApplyProviderAnswer(checkout.OnlyId(), succeeded));
        Assert.Equal(PaymentOrderStatus.Refunded, checkout.Only().Status);
    }

    [Fact]
    public void Only_an_applied_answer_raises_a_domain_event()
    {
        var checkout = Checkouts.SingleOrderIn(PaymentOrderStatus.Success);
        checkout.ClearDomainEvents();

        checkout.ApplyProviderAnswer(checkout.OnlyId(), succeeded: true);
        checkout.ApplyProviderAnswer(checkout.OnlyId(), succeeded: false);

        Assert.Empty(checkout.DomainEvents);
    }
}
