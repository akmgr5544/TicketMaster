using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentDomain;

public class RefundTests
{
    private const string Reference = "re_1";

    // --- The order ---

    [Fact]
    public void A_settled_order_is_refunded_with_the_providers_reference()
    {
        var checkout = Checkouts.SingleOrder();
        checkout.Settle(checkout.OnlyId());

        Assert.True(checkout.RefundOrder(checkout.OnlyId(), Reference));

        Assert.Equal(PaymentOrderStatus.Refunded, checkout.Only().Status);
        Assert.Equal(Reference, checkout.Only().RefundReference);
    }

    // The flag the caller reverses the money on: a redelivered refund must not reverse it twice.
    [Fact]
    public void Refunding_twice_is_a_no_op_the_second_time()
    {
        var checkout = Checkouts.SingleOrder();
        checkout.Settle(checkout.OnlyId());
        checkout.RefundOrder(checkout.OnlyId(), Reference);
        var before = Checkouts.Capture(checkout);

        Assert.False(checkout.RefundOrder(checkout.OnlyId(), "re_other"));

        Assert.Equal(before, Checkouts.Capture(checkout));
    }

    [Theory]
    [InlineData(PaymentOrderStatus.NotStarted)]
    [InlineData(PaymentOrderStatus.Executing)]
    [InlineData(PaymentOrderStatus.Failed)]
    public void An_order_that_never_succeeded_has_nothing_to_refund(PaymentOrderStatus status)
    {
        var checkout = Checkouts.SingleOrderIn(status);
        var before = Checkouts.Capture(checkout);

        Assert.Throws<PaymentDomainException>(() => checkout.RefundOrder(checkout.OnlyId(), Reference));

        Assert.Equal(before, Checkouts.Capture(checkout));
    }

    // Its wallet was never credited and its ledger pair never written, so there is nothing yet to reverse.
    [Fact]
    public void A_success_whose_settlement_is_not_recorded_is_refused()
    {
        var checkout = Checkouts.SingleOrderIn(PaymentOrderStatus.Success);

        Assert.Throws<PaymentDomainException>(() => checkout.RefundOrder(checkout.OnlyId(), Reference));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_refund_needs_a_reference(string reference)
    {
        var checkout = Checkouts.SingleOrder();
        checkout.Settle(checkout.OnlyId());

        Assert.Throws<PaymentDomainException>(() => checkout.RefundOrder(checkout.OnlyId(), reference));
    }

    [Fact]
    public void A_refunded_order_cannot_succeed_fail_or_start_again()
    {
        var checkout = Checkouts.SingleOrder();
        checkout.Settle(checkout.OnlyId());
        checkout.RefundOrder(checkout.OnlyId(), Reference);

        Assert.Throws<PaymentDomainException>(() => checkout.SucceedOrder(checkout.OnlyId()));
        Assert.Throws<PaymentDomainException>(() => checkout.FailOrder(checkout.OnlyId()));
        Assert.Throws<PaymentDomainException>(() => checkout.StartExecuting(checkout.OnlyId(), Checkouts.Provider, "other"));
        Assert.Equal(0, checkout.Cancel());
        Assert.Equal(PaymentOrderStatus.Refunded, checkout.Only().Status);
    }

    // --- The checkout ---

    [Fact]
    public void A_checkout_is_fully_refunded_only_once_every_paid_order_is()
    {
        var checkout = Checkouts.WithLines(Checkouts.Line(), Checkouts.Line());
        var (first, second) = (checkout.Ids()[0], checkout.Ids()[1]);
        checkout.Settle(first);
        checkout.Settle(second);

        checkout.RefundOrder(first, Reference);
        Assert.False(checkout.IsFullyRefunded);

        checkout.RefundOrder(second, "re_2");
        Assert.True(checkout.IsFullyRefunded);
        Assert.False(checkout.IsPaymentDone);
    }

    // A failed order took no money, so it does not hold a refund back.
    [Fact]
    public void A_failed_order_does_not_stop_the_checkout_counting_as_refunded()
    {
        var checkout = Checkouts.WithLines(Checkouts.Line(), Checkouts.Line());
        checkout.Settle(checkout.Ids()[0]);
        checkout.Drive(checkout.Ids()[1], PaymentOrderStatus.Failed);

        checkout.RefundOrder(checkout.Ids()[0], Reference);

        Assert.True(checkout.IsFullyRefunded);
    }

    [Fact]
    public void A_checkout_with_nothing_refunded_is_not_fully_refunded()
    {
        var checkout = Checkouts.SingleOrderIn(PaymentOrderStatus.Failed);

        Assert.False(checkout.IsFullyRefunded);
    }

    // --- The ledger ---

    [Fact]
    public void The_refund_pair_mirrors_the_pay_in_and_keeps_the_ledger_balanced()
    {
        var checkout = Checkouts.SingleOrder();
        checkout.Settle(checkout.OnlyId());
        var order = checkout.Only();
        var payIn = LedgerEntry.RecordPayIn(order);
        checkout.RefundOrder(order.PaymentOrderId, Reference);

        var refund = LedgerEntry.RecordRefund(order);

        var debit = Assert.Single(refund, e => e.Type == EntryType.Debit);
        var credit = Assert.Single(refund, e => e.Type == EntryType.Credit);
        Assert.Equal(order.MerchantId, debit.AccountId);
        Assert.Equal(order.BuyerId, credit.AccountId);
        Assert.All(refund, e => Assert.Equal(EntryReason.Refund, e.Reason));
        Assert.All(payIn, e => Assert.Equal(EntryReason.PayIn, e.Reason));
        Assert.Equal(0m, payIn.Concat(refund).Sum(e => e.Type == EntryType.Debit ? e.Amount : -e.Amount));
    }

    [Fact]
    public void Only_a_refunded_order_has_its_refund_written_to_the_ledger()
    {
        var checkout = Checkouts.SingleOrder();
        checkout.Settle(checkout.OnlyId());

        Assert.Throws<PaymentDomainException>(() => LedgerEntry.RecordRefund(checkout.Only()));
    }

    // --- The wallet ---

    [Fact]
    public void A_debit_takes_the_amount_off_the_balance()
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "USD");
        wallet.Credit(30m, "USD");

        wallet.Debit(12.50m, "USD");

        Assert.Equal(17.50m, wallet.Balance);
    }

    // The provider has already returned the money; refusing to record it would leave the wallet overstated.
    [Fact]
    public void A_debit_may_take_the_balance_below_zero()
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "USD");

        wallet.Debit(5m, "USD");

        Assert.Equal(-5m, wallet.Balance);
    }

    [Fact]
    public void A_debit_in_another_currency_is_refused()
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "USD");
        wallet.Credit(30m, "USD");

        Assert.Throws<PaymentDomainException>(() => wallet.Debit(5m, "EUR"));
        Assert.Equal(30m, wallet.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_debit_must_be_positive(decimal amount)
    {
        var wallet = Wallet.Create(Guid.NewGuid(), "USD");

        Assert.Throws<PaymentDomainException>(() => wallet.Debit(amount, "USD"));
    }
}
