using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.RefundOrders;

public sealed class RecordRefundOutcomeTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> RecordAsync(Guid refundOrderId, bool accepted, string? reference = "re_psp") =>
        HandleAsync<RecordRefundOutcomeHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(new RecordRefundOutcome(refundOrderId, accepted, reference, accepted ? null : "declined"),
                context, CancellationToken.None));

    // A paid order whose seller's wallet holds what the settlement credited.
    private async Task<(PaymentEvent Checkout, PaymentOrder Order)> SeedPaidAsync(decimal amount = 100m)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: amount);
        var order = checkout.PaymentOrders.Single();
        await SeedWalletAsync(order.SellerId, amount);
        return (checkout, order);
    }

    private Task<Wallet> ReadWalletAsync(Guid userId) => ReadAsync(c => c.Wallets.SingleAsync(w => w.UserId == userId));

    [Fact]
    public async Task An_accepted_refund_reverses_the_money_beside_the_pay_in()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 40m);

        await RecordAsync(refund.RefundOrderId, accepted: true, reference: "re_123");

        var stored = await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId));
        Assert.Equal(RefundOrderStatus.Succeeded, stored.Status);
        Assert.Equal("re_123", stored.PspRefundReference);
        Assert.True(stored.WalletReversed);
        Assert.True(stored.LedgerReversed);

        Assert.Equal(60m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);

        var entries = await ReadAsync(c => c.LedgerEntries.Where(l => l.TransactionRefId == refund.RefundOrderId).ToListAsync());
        Assert.Equal(2, entries.Count);
        var debit = Assert.Single(entries, e => e.EntryType == EntryType.Debit);
        var credit = Assert.Single(entries, e => e.EntryType == EntryType.Credit);
        Assert.Equal(order.SellerId.ToString(), debit.AccountId);
        Assert.Equal(checkout.BuyerId.ToString(), credit.AccountId);
        Assert.All(entries, e =>
        {
            Assert.Equal(40m, e.Amount.Amount);
            Assert.Equal(RefType.Refund, e.RefType);
        });
    }

    [Fact]
    public async Task A_partial_refund_is_reported_at_once_with_bookings_refund_id()
    {
        var (checkout, order) = await SeedPaidAsync();
        var refundId = Guid.NewGuid();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 40m, refundId);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true);

        var refunded = Assert.Single(outgoing.OfType<BookingRefundedIntegrationEvent>());
        Assert.Equal(checkout.BookingId, refunded.BookingId);
        Assert.Equal(refundId, refunded.RefundId);
        Assert.Equal(PaymentOrderStatus.Success, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
    }

    [Fact]
    public async Task A_partial_refund_is_reported_even_while_another_refund_is_still_pending()
    {
        var (_, order) = await SeedPaidAsync();
        await SeedRefundAsync(order.PaymentOrderId, 10m);
        var refundId = Guid.NewGuid();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 40m, refundId);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true);

        Assert.Equal(refundId, Assert.Single(outgoing.OfType<BookingRefundedIntegrationEvent>()).RefundId);
    }

    [Fact]
    public async Task Refunding_the_whole_amount_marks_the_order_refunded_and_reports_it()
    {
        var (checkout, order) = await SeedPaidAsync(80m);
        var refundId = Guid.NewGuid();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 80m, refundId);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true);

        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Equal(refundId, Assert.Single(outgoing.OfType<BookingRefundedIntegrationEvent>()).RefundId);
    }

    [Fact]
    public async Task Parts_that_reach_the_whole_amount_mark_the_order_refunded()
    {
        var (checkout, order) = await SeedPaidAsync(100m);
        await SeedSettledRefundAsync(order.PaymentOrderId, 60m);
        var refund = await SeedRefundAsync(order.PaymentOrderId, 40m);

        await RecordAsync(refund.RefundOrderId, accepted: true);

        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
    }

    [Fact]
    public async Task A_whole_booking_refund_is_reported_only_once_no_paid_order_is_left()
    {
        var checkout = await SeedCheckoutWithOrdersAsync(Guid.NewGuid(), NewBookingId(),
            (OrderState.Success, 40m), (OrderState.Success, 60m));
        var first = checkout.PaymentOrders.ElementAt(0);
        var second = checkout.PaymentOrders.ElementAt(1);
        await SeedWalletAsync(first.SellerId, 40m);
        await SeedWalletAsync(second.SellerId, 60m);
        var refundId = Guid.NewGuid();
        var firstRefund = await SeedRefundAsync(first.PaymentOrderId, 40m, refundId);
        var secondRefund = await SeedRefundAsync(second.PaymentOrderId, 60m, refundId);

        var afterFirst = await RecordAsync(firstRefund.RefundOrderId, accepted: true);
        var afterSecond = await RecordAsync(secondRefund.RefundOrderId, accepted: true);

        Assert.Empty(afterFirst.OfType<BookingRefundedIntegrationEvent>());
        var refunded = Assert.Single(afterSecond.OfType<BookingRefundedIntegrationEvent>());
        Assert.Equal(checkout.BookingId, refunded.BookingId);
        Assert.Equal(refundId, refunded.RefundId);
    }

    [Fact]
    public async Task A_whole_booking_refund_with_a_part_that_failed_is_never_reported_refunded()
    {
        var checkout = await SeedCheckoutWithOrdersAsync(Guid.NewGuid(), NewBookingId(),
            (OrderState.Success, 40m), (OrderState.Success, 60m));
        var first = checkout.PaymentOrders.ElementAt(0);
        var second = checkout.PaymentOrders.ElementAt(1);
        await SeedWalletAsync(first.SellerId, 40m);
        await SeedWalletAsync(second.SellerId, 60m);
        var refundId = Guid.NewGuid();
        var firstRefund = await SeedRefundAsync(first.PaymentOrderId, 40m, refundId);
        var secondRefund = await SeedRefundAsync(second.PaymentOrderId, 60m, refundId);

        var afterRefused = await RecordAsync(firstRefund.RefundOrderId, accepted: false);
        var afterAccepted = await RecordAsync(secondRefund.RefundOrderId, accepted: true);

        Assert.Empty(afterRefused);
        Assert.Empty(afterAccepted.OfType<BookingRefundedIntegrationEvent>());
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
        var refund = await SeedRefundAsync(order.PaymentOrderId, 40m);
        await RecordAsync(refund.RefundOrderId, accepted: true);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: true);

        Assert.Empty(outgoing);
        Assert.Equal(60m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Equal(2, await ReadAsync(c => c.LedgerEntries.CountAsync(l => l.TransactionRefId == refund.RefundOrderId)));
    }

    [Fact]
    public async Task A_refused_refund_moves_no_money_reports_nothing_and_is_left_for_a_person()
    {
        var (_, order) = await SeedPaidAsync();
        var refund = await SeedRefundAsync(order.PaymentOrderId, 40m);

        var outgoing = await RecordAsync(refund.RefundOrderId, accepted: false);

        Assert.Empty(outgoing);
        var stored = await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId));
        Assert.Equal(RefundOrderStatus.Failed, stored.Status);
        Assert.False(stored.WalletReversed);
        Assert.False(stored.LedgerReversed);
        Assert.Equal(100m, (await ReadWalletAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Empty(await ReadAsync(c => c.LedgerEntries.ToListAsync()));
        Assert.Contains(Logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }
}
