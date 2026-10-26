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
    public void A_settled_refund_records_when_it_succeeded()
    {
        var refund = NewRefund();
        var at = new DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);

        refund.MarkAsSettled("re_123", at);

        Assert.Equal(at, refund.SucceededAt);
    }

    [Fact]
    public void An_unanswered_refund_has_no_success_time()
    {
        var refund = NewRefund();

        refund.MarkAsProcessing("re_123", DateTime.UtcNow);

        Assert.Null(refund.SucceededAt);
    }

    [Fact]
    public void A_refund_marked_failed_has_failed()
    {
        var refund = NewRefund();

        refund.MarkAsFailed();

        Assert.Equal(RefundOrderStatus.Failed, refund.Status);
    }

    [Fact]
    public void A_refund_the_psp_accepted_without_finishing_is_processing_and_has_moved_no_money()
    {
        var refund = NewRefund();
        var at = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        refund.MarkAsProcessing("re_pending", at);

        Assert.Equal(RefundOrderStatus.Processing, refund.Status);
        Assert.Equal("re_pending", refund.PspRefundReference);
        Assert.Equal(at, refund.ProcessingSince);
        Assert.False(refund.WalletReversed);
        Assert.False(refund.LedgerReversed);
    }

    [Fact]
    public void A_processing_refund_can_still_succeed_or_fail()
    {
        var succeeding = NewRefund();
        succeeding.MarkAsProcessing("re_1", DateTime.UtcNow);
        succeeding.MarkAsSettled("re_1");

        var failing = NewRefund();
        failing.MarkAsProcessing("re_2", DateTime.UtcNow);
        failing.MarkAsFailed();

        Assert.Equal(RefundOrderStatus.Succeeded, succeeding.Status);
        Assert.Equal(RefundOrderStatus.Failed, failing.Status);
    }

    [Fact]
    public void Only_a_pending_refund_can_start_processing()
    {
        var processing = NewRefund();
        processing.MarkAsProcessing("re_1", DateTime.UtcNow);
        var settled = NewRefund();
        settled.MarkAsSettled("re_2");

        Assert.Throws<PaymentDomainException>(() => processing.MarkAsProcessing("re_1", DateTime.UtcNow));
        Assert.Throws<PaymentDomainException>(() => settled.MarkAsProcessing("re_2", DateTime.UtcNow));
    }

    [Fact]
    public void An_answered_refund_cannot_be_answered_again()
    {
        var settled = NewRefund();
        settled.MarkAsSettled("re_1");
        var failed = NewRefund();
        failed.MarkAsFailed();

        Assert.Throws<PaymentDomainException>(() => settled.MarkAsSettled("re_1"));
        Assert.Throws<PaymentDomainException>(() => settled.MarkAsFailed());
        Assert.Throws<PaymentDomainException>(() => failed.MarkAsSettled("re_2"));
        Assert.Throws<PaymentDomainException>(() => failed.MarkAsFailed());
    }

    [Fact]
    public void A_settled_refund_that_fails_afterwards_ends_failed_with_nothing_reversed()
    {
        var refund = NewRefund();
        refund.MarkAsSettled("re_1");
        refund.ConfirmWalletReversal();
        refund.ConfirmLedgerReversal();

        refund.MarkAsFailedAfterSettlement();

        Assert.Equal(RefundOrderStatus.Failed, refund.Status);
        Assert.False(refund.WalletReversed);
        Assert.False(refund.LedgerReversed);
    }

    [Fact]
    public void Only_a_succeeded_refund_can_fail_after_settling()
    {
        var pending = NewRefund();
        var processing = NewRefund();
        processing.MarkAsProcessing("re_1", DateTime.UtcNow);
        var failed = NewRefund();
        failed.MarkAsFailed();

        Assert.Throws<PaymentDomainException>(() => pending.MarkAsFailedAfterSettlement());
        Assert.Throws<PaymentDomainException>(() => processing.MarkAsFailedAfterSettlement());
        Assert.Throws<PaymentDomainException>(() => failed.MarkAsFailedAfterSettlement());
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
