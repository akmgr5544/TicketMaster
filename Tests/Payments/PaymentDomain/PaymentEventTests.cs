using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentDomain;

public sealed class PaymentEventTests
{
    private const string Provider = "Stripe";
    private const string Reference = "pi_123";

    private static PaymentEvent NewCheckout(params decimal[] amounts)
    {
        var checkout = PaymentEvent.Create(42, Guid.NewGuid(), "USD");
        foreach (var amount in amounts)
            checkout.AddPaymentOrder(Guid.NewGuid(), amount);
        return checkout;
    }

    private static Guid OnlyOrder(PaymentEvent checkout) => checkout.PaymentOrders.Single().PaymentOrderId;

    private static PaymentEvent CheckoutWithOrderIn(PaymentOrderStatus status)
    {
        var checkout = NewCheckout(100m);
        var id = OnlyOrder(checkout);
        if (status == PaymentOrderStatus.Init)
            return checkout;

        checkout.StartOrder(id, Provider, Reference);
        switch (status)
        {
            case PaymentOrderStatus.Success:
                checkout.SucceedOrder(id, Reference);
                break;
            case PaymentOrderStatus.Failed:
                checkout.FailOrder(id);
                break;
            case PaymentOrderStatus.Refunded:
                checkout.SucceedOrder(id, Reference);
                checkout.MarkOrderRefunded(id);
                break;
        }
        return checkout;
    }

    // C1
    [Fact]
    public void A_new_checkout_owes_nothing_yet()
    {
        var buyer = Guid.NewGuid();

        var checkout = PaymentEvent.Create(42, buyer, "usd");

        Assert.Equal(42, checkout.BookingId);
        Assert.Equal(buyer, checkout.BuyerId);
        Assert.Equal(Money.Zero("USD"), checkout.TotalAmount);
        Assert.False(checkout.IsPaymentDone);
        Assert.Null(checkout.PaymentMethodId);
        Assert.Empty(checkout.PaymentOrders);
    }

    [Fact]
    public void A_checkout_in_an_invalid_currency_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => PaymentEvent.Create(42, Guid.NewGuid(), "DOLLARS"));

    // C2
    [Fact]
    public void An_order_is_kept_in_the_checkouts_currency_and_starts_in_init()
    {
        var checkout = PaymentEvent.Create(42, Guid.NewGuid(), "USD");
        var seller = Guid.NewGuid();

        checkout.AddPaymentOrder(seller, 25.50m);

        var order = checkout.PaymentOrders.Single();
        Assert.Equal(seller, order.SellerId);
        Assert.Equal(Money.Create(25.50m, "USD"), order.Amount);
        Assert.Equal(PaymentOrderStatus.Init, order.Status);
        Assert.Equal(checkout.CheckoutId, order.CheckoutId);
    }

    [Fact]
    public void The_total_is_the_sum_of_its_orders() =>
        Assert.Equal(Money.Create(35.75m, "USD"), NewCheckout(25.50m, 10.25m).TotalAmount);

    [Fact]
    public void An_order_for_nothing_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => NewCheckout(0m));

    [Fact]
    public void A_negative_order_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => NewCheckout(-5m));

    [Fact]
    public void An_order_finer_than_a_cent_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => NewCheckout(10.001m));

    [Fact]
    public void A_refused_order_leaves_the_checkout_unchanged()
    {
        var checkout = NewCheckout(10m);

        Assert.Throws<PaymentDomainException>(() => checkout.AddPaymentOrder(Guid.NewGuid(), 0m));

        Assert.Single(checkout.PaymentOrders);
        Assert.Equal(Money.Create(10m, "USD"), checkout.TotalAmount);
    }

    [Fact]
    public void No_order_can_be_added_once_the_checkout_is_paid()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Success);
        Assert.True(checkout.IsPaymentDone);

        Assert.Throws<PaymentDomainException>(() => checkout.AddPaymentOrder(Guid.NewGuid(), 5m));
    }

    // C3
    [Fact]
    public void A_checkout_with_no_orders_cannot_be_completed() =>
        Assert.Throws<PaymentDomainException>(() => PaymentEvent.Create(42, Guid.NewGuid(), "USD").MarkAsComplete());

    // C4
    [Theory]
    [InlineData(PaymentOrderStatus.Init)]
    [InlineData(PaymentOrderStatus.Executing)]
    public void Cancelling_fails_an_order_still_waiting_on_the_buyer(PaymentOrderStatus status)
    {
        var checkout = CheckoutWithOrderIn(status);

        var failed = checkout.Cancel();

        Assert.Equal(1, failed);
        Assert.Equal(PaymentOrderStatus.Failed, checkout.PaymentOrders.Single().Status);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Refunded)]
    public void Cancelling_leaves_a_settled_order_alone(PaymentOrderStatus status)
    {
        var checkout = CheckoutWithOrderIn(status);

        var failed = checkout.Cancel();

        Assert.Equal(0, failed);
        Assert.Equal(status, checkout.PaymentOrders.Single().Status);
    }

    [Fact]
    public void Cancelling_counts_only_the_orders_it_failed()
    {
        var checkout = NewCheckout(10m, 20m, 30m);
        var paid = checkout.PaymentOrders.ElementAt(0).PaymentOrderId;
        checkout.StartOrder(paid, Provider, Reference);
        checkout.SucceedOrder(paid, Reference);
        checkout.StartOrder(checkout.PaymentOrders.ElementAt(1).PaymentOrderId, Provider, Reference);

        Assert.Equal(2, checkout.Cancel());
        Assert.Equal(PaymentOrderStatus.Success, checkout.FindOrder(paid).Status);
    }

    [Fact]
    public void Cancelling_twice_fails_nothing_the_second_time()
    {
        var checkout = NewCheckout(10m);
        checkout.Cancel();

        Assert.Equal(0, checkout.Cancel());
    }

    // C5
    [Fact]
    public void Starting_an_order_records_its_provider_and_reference()
    {
        var checkout = NewCheckout(10m);
        var id = OnlyOrder(checkout);

        var update = checkout.StartOrder(id, Provider, Reference);

        var order = checkout.FindOrder(id);
        Assert.Equal(OrderUpdate.Applied, update);
        Assert.Equal(PaymentOrderStatus.Executing, order.Status);
        Assert.Equal(Provider, order.Provider);
        Assert.Equal(Reference, order.PspTokenRef);
    }

    [Fact]
    public void Starting_again_through_the_same_provider_changes_nothing()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Executing);
        var id = OnlyOrder(checkout);

        var update = checkout.StartOrder(id, Provider, "pi_other");

        Assert.Equal(OrderUpdate.AlreadyApplied, update);
        Assert.Equal(Reference, checkout.FindOrder(id).PspTokenRef);
        Assert.Equal(PaymentOrderStatus.Executing, checkout.FindOrder(id).Status);
    }

    [Fact]
    public void Starting_through_a_different_provider_is_refused()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Executing);

        Assert.Throws<PaymentDomainException>(() => checkout.StartOrder(OnlyOrder(checkout), "Braintree", null));
        Assert.Equal(Provider, checkout.PaymentOrders.Single().Provider);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Refunded)]
    public void A_settled_order_cannot_be_started(PaymentOrderStatus status)
    {
        var checkout = CheckoutWithOrderIn(status);

        Assert.Throws<PaymentDomainException>(() => checkout.StartOrder(OnlyOrder(checkout), Provider, Reference));
        Assert.Equal(status, checkout.PaymentOrders.Single().Status);
    }

    [Fact]
    public void Starting_an_order_from_another_checkout_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => NewCheckout(10m).StartOrder(Guid.NewGuid(), Provider, Reference));

    // C6
    [Fact]
    public void A_success_on_a_started_order_pays_it()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Executing);
        var id = OnlyOrder(checkout);

        var update = checkout.SucceedOrder(id, "pi_paid");

        Assert.Equal(OrderUpdate.Applied, update);
        Assert.Equal(PaymentOrderStatus.Success, checkout.FindOrder(id).Status);
        Assert.Equal("pi_paid", checkout.FindOrder(id).PspTokenRef);
    }

    [Fact]
    public void A_repeated_success_changes_nothing()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Success);
        var id = OnlyOrder(checkout);

        var update = checkout.SucceedOrder(id, "pi_again");

        Assert.Equal(OrderUpdate.AlreadyApplied, update);
        Assert.Equal(Reference, checkout.FindOrder(id).PspTokenRef);
        Assert.Equal(PaymentOrderStatus.Success, checkout.FindOrder(id).Status);
    }

    [Fact]
    public void A_success_after_the_order_failed_does_not_revive_it()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Failed);
        var id = OnlyOrder(checkout);

        var update = checkout.SucceedOrder(id, Reference);

        Assert.Equal(OrderUpdate.Superseded, update);
        Assert.Equal(PaymentOrderStatus.Failed, checkout.FindOrder(id).Status);
        Assert.False(checkout.IsPaymentDone);
    }

    [Fact]
    public void A_success_for_an_order_never_started_is_not_applied()
    {
        var checkout = NewCheckout(10m);
        var id = OnlyOrder(checkout);

        var update = checkout.SucceedOrder(id, Reference);

        Assert.Equal(OrderUpdate.NotStarted, update);
        Assert.Equal(PaymentOrderStatus.Init, checkout.FindOrder(id).Status);
    }

    [Fact]
    public void A_late_success_on_a_refunded_order_is_already_applied()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Refunded);
        var id = OnlyOrder(checkout);

        var update = checkout.SucceedOrder(id, Reference);

        Assert.Equal(OrderUpdate.AlreadyApplied, update);
        Assert.Equal(PaymentOrderStatus.Refunded, checkout.FindOrder(id).Status);
    }

    [Fact]
    public void A_checkout_is_paid_only_once_every_order_is_paid()
    {
        var checkout = NewCheckout(10m, 20m);
        var first = checkout.PaymentOrders.ElementAt(0).PaymentOrderId;
        var second = checkout.PaymentOrders.ElementAt(1).PaymentOrderId;
        checkout.StartOrder(first, Provider, Reference);
        checkout.StartOrder(second, Provider, Reference);

        checkout.SucceedOrder(first, Reference);
        Assert.False(checkout.IsPaymentDone);

        checkout.SucceedOrder(second, Reference);
        Assert.True(checkout.IsPaymentDone);
    }

    // C7
    [Theory]
    [InlineData(PaymentOrderStatus.Init)]
    [InlineData(PaymentOrderStatus.Executing)]
    public void A_failure_fails_an_unsettled_order(PaymentOrderStatus status)
    {
        var checkout = CheckoutWithOrderIn(status);
        var id = OnlyOrder(checkout);

        var update = checkout.FailOrder(id);

        Assert.Equal(OrderUpdate.Applied, update);
        Assert.Equal(PaymentOrderStatus.Failed, checkout.FindOrder(id).Status);
    }

    [Fact]
    public void A_repeated_failure_changes_nothing()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Failed);

        Assert.Equal(OrderUpdate.AlreadyApplied, checkout.FailOrder(OnlyOrder(checkout)));
        Assert.Equal(PaymentOrderStatus.Failed, checkout.PaymentOrders.Single().Status);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Refunded)]
    public void A_paid_order_does_not_change_its_mind(PaymentOrderStatus status)
    {
        var checkout = CheckoutWithOrderIn(status);

        Assert.Equal(OrderUpdate.Superseded, checkout.FailOrder(OnlyOrder(checkout)));
        Assert.Equal(status, checkout.PaymentOrders.Single().Status);
    }

    // C8
    [Fact]
    public void Settling_an_order_marks_both_its_wallet_and_its_ledger_done()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Success);
        var id = OnlyOrder(checkout);
        Assert.False(checkout.FindOrder(id).WalletUpdated);
        Assert.False(checkout.FindOrder(id).LedgerUpdated);

        checkout.MarkOrderSettled(id);

        Assert.True(checkout.FindOrder(id).WalletUpdated);
        Assert.True(checkout.FindOrder(id).LedgerUpdated);
    }

    [Fact]
    public void Marking_an_order_refunded_refunds_it()
    {
        var checkout = CheckoutWithOrderIn(PaymentOrderStatus.Success);

        checkout.MarkOrderRefunded(OnlyOrder(checkout));

        Assert.Equal(PaymentOrderStatus.Refunded, checkout.PaymentOrders.Single().Status);
    }

    [Fact]
    public void An_order_from_another_checkout_is_not_found() =>
        Assert.Throws<PaymentDomainException>(() => NewCheckout(10m).FindOrder(Guid.NewGuid()));
}
