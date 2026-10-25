using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentDomain;

public sealed class RefundOrderTests
{
    private static RefundOrder NewRefund() =>
        RefundOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Create(10m, "USD"), "Seats cancelled");

    [Fact]
    public void A_new_refund_is_pending_and_has_moved_no_money()
    {
        var order = Guid.NewGuid();
        var request = Guid.NewGuid();
        var amount = Money.Create(10m, "USD");

        var refund = RefundOrder.Create(order, request, amount, "Seats cancelled");

        Assert.Equal(order, refund.PaymentOrderId);
        Assert.Equal(request, refund.RequestId);
        Assert.Equal(amount, refund.RefundAmount);
        Assert.Equal("Seats cancelled", refund.RefundReason);
        Assert.Equal(RefundOrderStatus.Pending, refund.Status);
        Assert.False(refund.WalletReversed);
        Assert.False(refund.LedgerReversed);
    }

    [Fact]
    public void A_refund_of_nothing_is_refused() =>
        Assert.Throws<PaymentDomainException>(() =>
            RefundOrder.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Zero("USD"), "Nothing"));

    [Fact]
    public void A_settled_refund_has_succeeded_and_keeps_the_providers_reference()
    {
        var refund = NewRefund();

        refund.MarkAsSettled("re_123");

        Assert.Equal(RefundOrderStatus.Succeeded, refund.Status);
        Assert.Equal("re_123", refund.PspRefundReference);
    }

    [Fact]
    public void A_refund_marked_failed_has_failed()
    {
        var refund = NewRefund();

        refund.MarkAsFailed();

        Assert.Equal(RefundOrderStatus.Failed, refund.Status);
    }

    [Fact]
    public void Confirming_the_reversals_marks_the_wallet_and_ledger_reversed()
    {
        var refund = NewRefund();

        refund.ConfirmWalletReversal();
        refund.ConfirmLedgerReversal();

        Assert.True(refund.WalletReversed);
        Assert.True(refund.LedgerReversed);
    }
}
