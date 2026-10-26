using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.RefundOrders;

// Refunds are whole only, so an accepted one gives back everything left on the order and is reported to Bookings.
public sealed class RecordRefundOutcomeTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> RecordAsync(Guid refundOrderId, bool accepted, string? reference = "re_psp",
        bool final = true) =>
        HandleAsync<RecordRefundOutcomeHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(new RecordRefundOutcome(refundOrderId, accepted, reference, accepted ? null : "declined", final),
                context, CancellationToken.None));

    private Task<RefundOrder> ReadRefundAsync(Guid refundOrderId) =>
        ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refundOrderId));

    private Task<List<LedgerEntry>> ReadLedgerAsync(Guid refundOrderId) =>
        ReadAsync(c => c.LedgerEntries.Where(l => l.TransactionRefId == refundOrderId).ToListAsync());

    // A paid order whose seller's wallet holds what the settlement credited.
    private async Task<(PaymentEvent Checkout, PaymentOrder Order)> SeedPaidAsync(decimal amount = 100m)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: amount);
        var order = checkout.PaymentOrders.Single();
        await SeedWalletAsync(order.SellerId, amount);
        return (checkout, order);
    }

    // The buyer hears the refund failed, and Bookings is never told it was refunded.
    private static void AssertBuyerToldRefundFailed(OutgoingMessages outgoing, long bookingId)
    {
        var failed = Assert.Single(outgoing);
        Assert.Equal(bookingId, Assert.IsType<BookingRefundFailedIntegrationEvent>(failed).BookingId);
    }

    [Fact]
    public async Task A_refused_refund_names_the_refund_bookings_asked_for()
    {
        var (_, order) = await SeedPaidAsync();
        var refundId = Guid.NewGuid();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m, refundId);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: false);

        Assert.Equal(refundId, Assert.IsType<BookingRefundFailedIntegrationEvent>(Assert.Single(outgoing)).RefundId);
    }

    private Task<Wallet> ReadWalletAsync(Guid userId) => ReadAsync(c => c.Wallets.SingleAsync(w => w.UserId == userId));

    [Fact]
    public async Task An_accepted_refund_reverses_the_money_beside_the_pay_in()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);

        await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_123");

        var stored = await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId));
        Assert.Equal(RefundOrderStatus.Succeeded, stored.Status);
        Assert.Equal("re_123", stored.PspRefundReference);
        Assert.True(stored.WalletReversed);
        Assert.True(stored.LedgerReversed);

        Assert.Equal(0m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);

        var entries = await ReadAsync(c => c.LedgerEntries.Where(l => l.TransactionRefId == refund.RefundOrderId).ToListAsync());
        Assert.Equal(2, entries.Count);
        var debit = Assert.Single(entries, e => e.EntryType == EntryType.Debit);
        var credit = Assert.Single(entries, e => e.EntryType == EntryType.Credit);
        Assert.Equal(order.SellerId.ToString(), debit.AccountId);
        Assert.Equal(checkout.BuyerId.ToString(), credit.AccountId);
        Assert.All(entries, e =>
        {
            Assert.Equal(100m, e.Amount.Amount);
            Assert.Equal(RefType.Refund, e.RefType);
        });
    }

    [Fact]
    public async Task An_accepted_refund_marks_the_order_refunded_and_reports_bookings_refund_id()
    {
        var (checkout, order) = await SeedPaidAsync(80m);
        var refundId = Guid.NewGuid();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 80m, refundId);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true);

        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        var refunded = Assert.Single(outgoing.OfType<BookingRefundedIntegrationEvent>());
        Assert.Equal(checkout.BookingId, refunded.BookingId);
        Assert.Equal(refundId, refunded.RefundId);
    }

    [Fact]
    public async Task A_refund_accepted_after_an_earlier_attempt_failed_still_refunds_everything_and_is_reported()
    {
        var (checkout, order) = await SeedPaidAsync(100m);
        var failed = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(failed.RefundOrderId, accepted: false);
        var refundId = Guid.NewGuid();
        var retry = await SeedRefundAsync(order.PaymentOrderId, 100m, refundId);

        var outgoing = await RecordAsync(retry.RefundOrderId, accepted: true);

        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Equal(0m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Equal(refundId, Assert.Single(outgoing.OfType<BookingRefundedIntegrationEvent>()).RefundId);
    }

    [Fact]
    public async Task A_refund_nobody_named_is_reported_without_a_refund_id()
    {
        var (checkout, order) = await SeedPaidAsync(50m);
        var refund = await SeedRefundAsync(order.PaymentOrderId, 50m, requestId: order.PaymentOrderId);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true);

        var refunded = Assert.Single(outgoing.OfType<BookingRefundedIntegrationEvent>());
        Assert.Equal(checkout.BookingId, refunded.BookingId);
        Assert.Null(refunded.RefundId);
    }

    [Fact]
    public async Task A_redelivered_outcome_moves_no_money_and_reports_nothing()
    {
        var (_, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: true);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true);

        Assert.Empty(outgoing);
        Assert.Equal(0m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Equal(2, await ReadAsync(c => c.LedgerEntries.CountAsync(l => l.TransactionRefId == refund.RefundOrderId)));
    }

    [Fact]
    public async Task A_refused_refund_moves_no_money_tells_the_buyer_and_is_left_for_a_person()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: false);

        AssertBuyerToldRefundFailed(outgoing, checkout.BookingId);
        var stored = await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId));
        Assert.Equal(RefundOrderStatus.Failed, stored.Status);
        Assert.False(stored.WalletReversed);
        Assert.False(stored.LedgerReversed);
        Assert.Equal(100m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Empty(await ReadAsync(c => c.LedgerEntries.ToListAsync()));
        Assert.Equal(PaymentOrderStatus.Success, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Contains(Logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }

    // R1 — the PSP took the refund but has not finished it: nothing has happened to the money yet.
    [Fact]
    public async Task A_refund_the_psp_has_not_finished_is_processing_and_moves_nothing()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_pending", final: false);

        Assert.Empty(outgoing);
        var stored = await ReadRefundAsync(refund.RefundOrderId);
        Assert.Equal(RefundOrderStatus.Processing, stored.Status);
        Assert.Equal("re_pending", stored.PspRefundReference);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, stored.ProcessingSince);
        Assert.Equal(100m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Empty(await ReadLedgerAsync(refund.RefundOrderId));
        Assert.Equal(PaymentOrderStatus.Success, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
    }

    [Fact]
    public async Task A_second_not_finished_answer_changes_nothing()
    {
        var (_, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_pending", final: false);
        var since = (await ReadRefundAsync(refund.RefundOrderId)).ProcessingSince;
        Clock.Advance(TimeSpan.FromMinutes(5));

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_other", final: false);

        Assert.Empty(outgoing);
        var stored = await ReadRefundAsync(refund.RefundOrderId);
        Assert.Equal(RefundOrderStatus.Processing, stored.Status);
        Assert.Equal("re_pending", stored.PspRefundReference);
        Assert.Equal(since, stored.ProcessingSince);
    }

    [Fact]
    public async Task A_processing_refund_that_then_succeeds_reverses_the_money_and_is_reported()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refundId = Guid.NewGuid();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m, refundId);
        await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_pending", final: false);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_pending");

        Assert.Equal(RefundOrderStatus.Succeeded, (await ReadRefundAsync(refund.RefundOrderId)).Status);
        Assert.Equal(0m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Equal(2, (await ReadLedgerAsync(refund.RefundOrderId)).Count);
        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Equal(refundId, Assert.Single(outgoing.OfType<BookingRefundedIntegrationEvent>()).RefundId);
    }

    [Fact]
    public async Task A_processing_refund_that_then_fails_moves_nothing_and_is_left_for_a_person()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_pending", final: false);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: false);

        AssertBuyerToldRefundFailed(outgoing, checkout.BookingId);
        Assert.Equal(RefundOrderStatus.Failed, (await ReadRefundAsync(refund.RefundOrderId)).Status);
        Assert.Equal(100m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Empty(await ReadLedgerAsync(refund.RefundOrderId));
        Assert.Equal(PaymentOrderStatus.Success, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Contains(Logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }

    // R2 — Braintree counts a credit submitted for settlement as done and the bank can still decline it; Stripe can
    // fail a refund it reported succeeded. The money never reached the buyer, so it is the seller's again.
    [Fact]
    public async Task A_refund_that_fails_after_succeeding_puts_the_money_back_and_is_raised_for_a_person()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_done");

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: false, reference: "re_done");

        AssertBuyerToldRefundFailed(outgoing, checkout.BookingId);
        var stored = await ReadRefundAsync(refund.RefundOrderId);
        Assert.Equal(RefundOrderStatus.Failed, stored.Status);
        Assert.False(stored.WalletReversed);
        Assert.False(stored.LedgerReversed);
        Assert.Equal(100m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Equal(PaymentOrderStatus.Success, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Contains(Logs.Lines, line => line.StartsWith("Critical") && line.Contains(refund.RefundOrderId.ToString()));
    }

    // The ledger is append-only: the refund's pair stays, and a pair the other way is written beside it.
    [Fact]
    public async Task Undoing_a_refund_adds_a_balancing_pair_rather_than_editing_the_first()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: true);
        var first = (await ReadLedgerAsync(refund.RefundOrderId)).Select(e => e.EntryId).ToHashSet();

        await RecordAsync(refund.RefundOrderId, accepted: false);

        var entries = await ReadLedgerAsync(refund.RefundOrderId);
        Assert.Equal(4, entries.Count);
        Assert.Superset(first, entries.Select(e => e.EntryId).ToHashSet());
        var seller = entries.Where(e => e.AccountId == order.SellerId.ToString()).ToList();
        var buyer = entries.Where(e => e.AccountId == checkout.BuyerId.ToString()).ToList();
        Assert.Equal(0m, seller.Sum(e => e.EntryType == EntryType.Credit ? e.Amount.Amount : -e.Amount.Amount));
        Assert.Equal(0m, buyer.Sum(e => e.EntryType == EntryType.Credit ? e.Amount.Amount : -e.Amount.Amount));
        Assert.Single(seller, e => e.EntryType == EntryType.Credit);
        Assert.Single(buyer, e => e.EntryType == EntryType.Debit);
    }

    [Fact]
    public async Task A_repeated_failure_after_succeeding_undoes_the_refund_once()
    {
        var (_, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: true);
        await RecordAsync(refund.RefundOrderId, accepted: false);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: false);

        Assert.Empty(outgoing);
        Assert.Equal(100m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Equal(4, (await ReadLedgerAsync(refund.RefundOrderId)).Count);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task An_answer_after_a_refund_failed_changes_nothing(bool accepted, bool final)
    {
        var (_, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: false);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted, final: final);

        Assert.Empty(outgoing);
        Assert.Equal(RefundOrderStatus.Failed, (await ReadRefundAsync(refund.RefundOrderId)).Status);
        Assert.Equal(100m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Empty(await ReadLedgerAsync(refund.RefundOrderId));
    }

    [Fact]
    public async Task A_late_not_finished_answer_after_success_changes_nothing()
    {
        var (_, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        await RecordAsync(refund.RefundOrderId, accepted: true);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true, final: false);

        Assert.Empty(outgoing);
        Assert.Equal(RefundOrderStatus.Succeeded, (await ReadRefundAsync(refund.RefundOrderId)).Status);
        Assert.Equal(0m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
    }
}
