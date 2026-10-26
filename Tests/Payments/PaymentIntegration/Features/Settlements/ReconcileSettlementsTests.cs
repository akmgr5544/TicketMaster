using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Features.Settlements;
using PaymentSystem.Shared.Messaging;
using Wolverine;

namespace PaymentIntegration.Features.Settlements;

// What each PSP settled, compared with our books: every difference is recorded once for a person, and the only thing
// corrected is a refund the PSP took back, which is handed to RecordRefundOutcome like a webhook's answer would be.
public sealed class ReconcileSettlementsTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private static readonly TimeSpan PastGrace = SettlementReconciler.Grace + TimeSpan.FromDays(1);

    private async Task<int> ReconcileAsync()
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<SettlementReconciler>().RunOnceAsync(CancellationToken.None);
    }

    private Task<List<SettlementDiscrepancy>> DiscrepanciesAsync() =>
        ReadAsync(c => c.SettlementDiscrepancies.ToListAsync());

    private Task<List<SettlementRecord>> RecordsAsync() => ReadAsync(c => c.SettlementRecords.ToListAsync());

    private void StripeSettles(params SettlementLine[] lines) => Psp.Stripe.OnSettlement = _ => lines;

    private int ErrorLogs() => Logs.Lines.Count(line => line.StartsWith("Error") && line.Contains("Settlement discrepancy"));

    private async Task<PaymentOrder> SeedPaidOrderAsync(decimal amount = 100m, string provider = "Stripe")
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: amount, provider: provider);
        return checkout.PaymentOrders.Single();
    }

    private static SettlementLine ChargeFor(PaymentOrder order, decimal? amount = null, string currency = Usd) =>
        new(StubGateway.ReferenceFor(order.PaymentOrderId), SettlementLineKind.Charge, amount ?? order.Amount.Amount, currency);

    // A refund in the given state, its PSP reference set and, when Succeeded, done at the test clock's time.
    private async Task<RefundOrder> SeedRefundInAsync(Guid paymentOrderId, RefundOrderStatus status, string reference,
        decimal amount = 100m)
    {
        var refund = await SeedRefundAsync(paymentOrderId, amount);
        await InScopeAsync(async c =>
        {
            var stored = await c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId);
            var now = Clock.GetUtcNow().UtcDateTime;
            if (status != RefundOrderStatus.Pending)
                stored.MarkAsProcessing(reference, now);
            if (status == RefundOrderStatus.Succeeded)
                stored.MarkAsSettled(reference, now);
            if (status == RefundOrderStatus.Failed)
                stored.MarkAsFailed();
            await c.SaveChangesAsync();
        });
        return refund;
    }

    [Fact]
    public async Task Asks_each_provider_for_the_last_eight_full_days()
    {
        await ReconcileAsync();

        var today = Clock.GetUtcNow().UtcDateTime.Date;
        var expected = new SettlementRequest(today - SettlementReconciler.Window, today);
        Assert.Equal(expected, Assert.Single(Psp.Stripe.SettlementRequests));
        Assert.Equal(expected, Assert.Single(Psp.Braintree.SettlementRequests));
    }

    [Fact]
    public async Task A_settlement_that_matches_our_books_is_recorded_and_raises_nothing()
    {
        var order = await SeedPaidOrderAsync();
        var refund = await SeedRefundInAsync(order.PaymentOrderId, RefundOrderStatus.Succeeded, "re_ok");
        StripeSettles(ChargeFor(order), new SettlementLine("re_ok", SettlementLineKind.Refund, 100m, Usd));

        var found = await ReconcileAsync();

        Assert.Equal(0, found);
        Assert.Empty(await DiscrepanciesAsync());
        var records = await RecordsAsync();
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.Kind == SettlementEntryKind.Refund && r.ProviderReference == "re_ok"
                                      && r.Amount.Amount == 100m && r.Provider == "Stripe");
        Assert.Empty(Bus.Invoked);
        _ = refund;
    }

    [Fact]
    public async Task Reading_the_same_days_again_adds_no_record_no_discrepancy_and_no_log()
    {
        var order = await SeedPaidOrderAsync();
        StripeSettles(ChargeFor(order), new SettlementLine("ch_stranger", SettlementLineKind.Charge, 5m, Usd));
        await ReconcileAsync();
        var logsAfterFirst = ErrorLogs();

        var found = await ReconcileAsync();

        Assert.Equal(0, found);
        Assert.Equal(2, (await RecordsAsync()).Count);
        Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(1, logsAfterFirst);
        Assert.Equal(logsAfterFirst, ErrorLogs());
    }

    [Fact]
    public async Task A_charge_for_no_order_of_ours_is_an_unknown_charge()
    {
        StripeSettles(new SettlementLine("pi_stranger", SettlementLineKind.Charge, 42m, Usd));

        Assert.Equal(1, await ReconcileAsync());

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.UnknownCharge, finding.Kind);
        Assert.Equal("pi_stranger", finding.Reference);
        Assert.Equal(42m, finding.ActualAmount);
        Assert.Equal(1, ErrorLogs());
    }

    [Fact]
    public async Task A_charge_for_an_order_we_count_failed_is_a_charge_for_an_unpaid_order()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Failed);
        var order = checkout.PaymentOrders.Single();
        StripeSettles(ChargeFor(order));

        await ReconcileAsync();

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.ChargeForUnpaidOrder, finding.Kind);
        Assert.Equal(order.PaymentOrderId, finding.PaymentOrderId);
    }

    [Theory]
    [InlineData(99.99, "USD")]
    [InlineData(100, "EUR")]
    public async Task A_charge_of_another_amount_or_currency_is_a_charge_amount_mismatch(decimal amount, string currency)
    {
        var order = await SeedPaidOrderAsync();
        StripeSettles(ChargeFor(order, amount, currency));

        await ReconcileAsync();

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.ChargeAmountMismatch, finding.Kind);
        Assert.Equal((100m, Usd, amount, currency),
            (finding.ExpectedAmount, finding.ExpectedCurrency, finding.ActualAmount, finding.ActualCurrency));
    }

    [Fact]
    public async Task A_charge_found_by_the_order_id_it_was_tagged_with_matches_when_the_reference_differs()
    {
        var order = await SeedPaidOrderAsync(provider: "Braintree");
        Psp.Braintree.OnSettlement = _ =>
            [new SettlementLine("bt_other", SettlementLineKind.Charge, 100m, Usd, order.PaymentOrderId)];

        Assert.Equal(0, await ReconcileAsync());
    }

    [Fact]
    public async Task A_refund_we_have_no_record_of_is_a_refund_not_recorded()
    {
        StripeSettles(new SettlementLine("re_stranger", SettlementLineKind.Refund, 10m, Usd));

        await ReconcileAsync();

        Assert.Equal(SettlementDiscrepancyKind.RefundNotRecorded, Assert.Single(await DiscrepanciesAsync()).Kind);
    }

    [Fact]
    public async Task A_refund_line_beside_our_failed_refund_with_no_failure_line_is_a_refund_not_recorded()
    {
        var order = await SeedPaidOrderAsync();
        var refund = await SeedRefundInAsync(order.PaymentOrderId, RefundOrderStatus.Failed, "re_gone");
        StripeSettles(ChargeFor(order), new SettlementLine("re_gone", SettlementLineKind.Refund, 100m, Usd));

        await ReconcileAsync();

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.RefundNotRecorded, finding.Kind);
        Assert.Equal(refund.RefundOrderId, finding.RefundOrderId);
    }

    // Stripe writes a pending refund's line at once, and a failure line if it later fails.
    [Fact]
    public async Task A_refund_line_beside_our_processing_refund_or_a_failed_one_with_its_failure_line_is_fine()
    {
        var processing = await SeedPaidOrderAsync();
        await SeedRefundInAsync(processing.PaymentOrderId, RefundOrderStatus.Processing, "re_pending");
        var failed = await SeedPaidOrderAsync();
        await SeedRefundInAsync(failed.PaymentOrderId, RefundOrderStatus.Failed, "re_failed");
        StripeSettles(
            ChargeFor(processing), ChargeFor(failed),
            new SettlementLine("re_pending", SettlementLineKind.Refund, 100m, Usd),
            new SettlementLine("re_failed", SettlementLineKind.Refund, 100m, Usd),
            new SettlementLine("re_failed", SettlementLineKind.RefundFailure, 100m, Usd));

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Bus.Invoked);
    }

    [Fact]
    public async Task A_refund_of_another_amount_is_a_refund_amount_mismatch()
    {
        var order = await SeedPaidOrderAsync();
        await SeedRefundInAsync(order.PaymentOrderId, RefundOrderStatus.Succeeded, "re_short");
        StripeSettles(ChargeFor(order), new SettlementLine("re_short", SettlementLineKind.Refund, 90m, Usd));

        await ReconcileAsync();

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.RefundAmountMismatch, finding.Kind);
        Assert.Equal((100m, 90m), (finding.ExpectedAmount, finding.ActualAmount));
    }

    [Fact]
    public async Task A_refund_the_psp_took_back_after_we_counted_it_done_is_recorded_and_undone()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var order = checkout.PaymentOrders.Single();
        await SeedWalletAsync(order.SellerId, 100m);
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        // Settled through the real handler, so the wallet, ledger and order are where a real refund leaves them.
        await HandleAsync<RecordRefundOutcomeHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(new RecordRefundOutcome(refund.RefundOrderId, true, "re_back", null), context, CancellationToken.None));
        StripeSettles(ChargeFor(order),
            new SettlementLine("re_back", SettlementLineKind.Refund, 100m, Usd),
            new SettlementLine("re_back", SettlementLineKind.RefundFailure, 100m, Usd));

        await ReconcileAsync();

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.RefundFailedAfterSuccess, finding.Kind);
        Assert.Equal(refund.RefundOrderId, finding.RefundOrderId);
        var outcome = Assert.IsType<RecordRefundOutcome>(Assert.Single(Bus.Invoked));
        Assert.Equal((refund.RefundOrderId, false, true), (outcome.RefundOrderId, outcome.Accepted, outcome.Final));

        // What Wolverine would do with it: the undo puts the money back on the seller's side.
        await HandleAsync<RecordRefundOutcomeHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(outcome, context, CancellationToken.None));
        Assert.Equal(RefundOrderStatus.Failed,
            (await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId))).Status);
        Assert.Equal(PaymentOrderStatus.Success, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Equal(100m, (await ReadAsync(c => c.Wallets.SingleAsync(w => w.UserId == order.SellerId))).CurrentBalance.Amount);
    }

    [Fact]
    public async Task A_failure_line_for_a_refund_still_processing_is_handed_on_without_a_discrepancy()
    {
        var order = await SeedPaidOrderAsync();
        var refund = await SeedRefundInAsync(order.PaymentOrderId, RefundOrderStatus.Processing, "re_slow");
        StripeSettles(ChargeFor(order),
            new SettlementLine("re_slow", SettlementLineKind.Refund, 100m, Usd),
            new SettlementLine("re_slow", SettlementLineKind.RefundFailure, 100m, Usd));

        await ReconcileAsync();

        Assert.Empty(await DiscrepanciesAsync());
        var outcome = Assert.IsType<RecordRefundOutcome>(Assert.Single(Bus.Invoked));
        Assert.Equal((refund.RefundOrderId, false), (outcome.RefundOrderId, outcome.Accepted));
    }

    [Fact]
    public async Task A_failure_line_for_a_refund_already_failed_changes_nothing()
    {
        var order = await SeedPaidOrderAsync();
        await SeedRefundInAsync(order.PaymentOrderId, RefundOrderStatus.Failed, "re_done");
        StripeSettles(ChargeFor(order),
            new SettlementLine("re_done", SettlementLineKind.Refund, 100m, Usd),
            new SettlementLine("re_done", SettlementLineKind.RefundFailure, 100m, Usd));

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Bus.Invoked);
    }

    [Fact]
    public async Task An_order_paid_past_the_grace_period_that_never_settled_is_an_order_not_settled()
    {
        var order = await SeedPaidOrderAsync();
        Clock.Advance(PastGrace);

        await ReconcileAsync();

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.OrderNotSettled, finding.Kind);
        Assert.Equal(StubGateway.ReferenceFor(order.PaymentOrderId), finding.Reference);
        Assert.Equal("Stripe", finding.Provider);
    }

    [Fact]
    public async Task An_order_still_inside_the_grace_period_is_not_flagged()
    {
        await SeedPaidOrderAsync();
        Clock.Advance(SettlementReconciler.Grace - TimeSpan.FromHours(1));

        Assert.Equal(0, await ReconcileAsync());
    }

    [Fact]
    public async Task An_order_whose_charge_was_settled_is_not_flagged_later()
    {
        var order = await SeedPaidOrderAsync();
        StripeSettles(ChargeFor(order));
        await ReconcileAsync();
        StripeSettles();
        Clock.Advance(PastGrace);

        Assert.Equal(0, await ReconcileAsync());
    }

    [Fact]
    public async Task A_refund_done_past_the_grace_period_that_never_settled_is_a_refund_not_settled()
    {
        var order = await SeedPaidOrderAsync();
        var refund = await SeedRefundInAsync(order.PaymentOrderId, RefundOrderStatus.Succeeded, "re_lost");
        StripeSettles(ChargeFor(order));
        await ReconcileAsync();
        Clock.Advance(PastGrace);

        await ReconcileAsync();

        var finding = Assert.Single(await DiscrepanciesAsync());
        Assert.Equal(SettlementDiscrepancyKind.RefundNotSettled, finding.Kind);
        Assert.Equal(refund.RefundOrderId, finding.RefundOrderId);
    }

    // Braintree refunds an unsettled sale by voiding it: neither the sale nor the void ever settles.
    [Fact]
    public async Task A_voided_sale_is_neither_an_order_nor_a_refund_not_settled()
    {
        var order = await SeedPaidOrderAsync(provider: "Braintree");
        await SeedRefundInAsync(order.PaymentOrderId, RefundOrderStatus.Succeeded, StubGateway.ReferenceFor(order.PaymentOrderId));
        Clock.Advance(PastGrace);

        Assert.Equal(0, await ReconcileAsync());
    }

    [Fact]
    public async Task A_provider_that_cannot_be_read_does_not_stop_the_other()
    {
        Psp.Stripe.OnSettlement = _ =>
            throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Transient, "down");
        Psp.Braintree.OnSettlement = _ => [new SettlementLine("bt_stranger", SettlementLineKind.Charge, 1m, Usd)];

        Assert.Equal(1, await ReconcileAsync());

        Assert.Equal("Braintree", Assert.Single(await DiscrepanciesAsync()).Provider);
        Assert.Contains(Logs.Lines, line => line.StartsWith("Warning") && line.Contains("Stripe's settlement"));
    }

    // An unread provider's orders are not checked: nothing was read to say they did not settle.
    [Fact]
    public async Task An_unread_providers_overdue_orders_are_not_flagged()
    {
        await SeedPaidOrderAsync();
        Psp.Stripe.OnSettlement = _ =>
            throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Transient, "down");
        Clock.Advance(PastGrace);

        Assert.Equal(0, await ReconcileAsync());
    }
}
