using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Domain;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Messaging;

namespace PaymentIntegration.Features.RefundOrders;

// The outcome is recorded through Wolverine, which this fixture does not run; what RecordRefundOutcome then does is
// RecordRefundOutcomeTests'. Here it is enough to see which processing refunds were asked about and what was handed on.
public sealed class ReconcileRefundsTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private static readonly TimeSpan PastStuck = RefundReconciler.StuckAfter + TimeSpan.FromSeconds(1);

    private async Task<int> ReconcileAsync()
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<RefundReconciler>().RunOnceAsync(CancellationToken.None);
    }

    // A refund the PSP accepted without finishing, as RecordRefundOutcome leaves it.
    private async Task<RefundOrder> SeedProcessingRefundAsync(string provider = "Stripe", string reference = "re_pending")
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, provider: provider);
        var refund = await SeedRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 100m);
        await InScopeAsync(async c =>
        {
            var stored = await c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId);
            stored.MarkAsProcessing(reference, Clock.GetUtcNow().UtcDateTime);
            await c.SaveChangesAsync();
        });
        return refund;
    }

    [Fact]
    public async Task A_processing_refund_the_psp_reports_done_is_recorded_as_succeeded()
    {
        var refund = await SeedProcessingRefundAsync();
        Psp.Stripe.OnLookupRefund = _ => new RefundResult("re_pending", RefundStatus.Succeeded);
        Clock.Advance(PastStuck);

        var recorded = await ReconcileAsync();

        Assert.Equal(1, recorded);
        Assert.Equal(new RecordRefundOutcome(refund.RefundOrderId, true, "re_pending", null), Assert.Single(Bus.Invoked));
        Assert.True(Psp.Stripe.RefundLookups.TryPeek(out var lookup));
        Assert.Equal(new RefundLookupRequest(refund.PaymentOrderId, "re_pending"), lookup);
    }

    [Fact]
    public async Task A_processing_refund_the_psp_reports_failed_is_recorded_as_refused_with_the_reason()
    {
        var refund = await SeedProcessingRefundAsync();
        Psp.Stripe.OnLookupRefund = _ => new RefundResult("re_pending", RefundStatus.Failed, "expired_or_canceled_card");
        Clock.Advance(PastStuck);

        Assert.Equal(1, await ReconcileAsync());
        Assert.Equal(new RecordRefundOutcome(refund.RefundOrderId, false, "re_pending", "expired_or_canceled_card"),
            Assert.Single(Bus.Invoked));
    }

    [Fact]
    public async Task The_refund_is_asked_about_at_the_psp_that_took_the_payment()
    {
        await SeedProcessingRefundAsync(provider: "Braintree", reference: "cr_1");
        Psp.Braintree.OnLookupRefund = _ => new RefundResult("cr_1", RefundStatus.Succeeded);
        Clock.Advance(PastStuck);

        Assert.Equal(1, await ReconcileAsync());
        Assert.Empty(Psp.Stripe.RefundLookups);
        Assert.Single(Psp.Braintree.RefundLookups);
    }

    [Fact]
    public async Task A_refund_still_pending_at_the_psp_is_left_for_the_next_run()
    {
        await SeedProcessingRefundAsync();
        Psp.Stripe.OnLookupRefund = _ => new RefundResult("re_pending", RefundStatus.Pending);
        Clock.Advance(PastStuck);

        Assert.Equal(0, await ReconcileAsync());
        Assert.Single(Psp.Stripe.RefundLookups);
        Assert.Empty(Bus.Invoked);
    }

    [Fact]
    public async Task A_refund_the_psp_does_not_know_is_left_alone()
    {
        await SeedProcessingRefundAsync();
        Clock.Advance(PastStuck);

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Bus.Invoked);
    }

    // A webhook on its way normally lands first; asking sooner would only race it.
    [Fact]
    public async Task A_refund_that_only_just_started_processing_is_not_asked_about_yet()
    {
        await SeedProcessingRefundAsync();
        Psp.Stripe.OnLookupRefund = _ => new RefundResult("re_pending", RefundStatus.Succeeded);
        Clock.Advance(RefundReconciler.StuckAfter - TimeSpan.FromSeconds(1));

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Psp.Stripe.RefundLookups);
    }

    // Pending belongs to the durable ProcessRefund message, and an answered refund is done.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task Only_processing_refunds_are_asked_about(bool answered, bool accepted)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var refund = await SeedRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 100m);
        if (answered)
        {
            await InScopeAsync(async c =>
            {
                var stored = await c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId);
                if (accepted)
                    stored.MarkAsSettled("re_done");
                else
                    stored.MarkAsFailed();
                await c.SaveChangesAsync();
            });
        }
        Psp.Stripe.OnLookupRefund = _ => new RefundResult("re_done", RefundStatus.Succeeded);
        Clock.Advance(PastStuck);

        Assert.Equal(0, await ReconcileAsync());
        Assert.Empty(Psp.Stripe.RefundLookups);
    }

    [Fact]
    public async Task A_psp_that_cannot_be_reached_for_one_refund_does_not_stop_the_others()
    {
        var unreachable = await SeedProcessingRefundAsync(reference: "re_unreachable");
        var answered = await SeedProcessingRefundAsync(reference: "re_answered");
        Psp.Stripe.OnLookupRefund = request => request.ProviderReference == "re_unreachable"
            ? throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Transient, "down")
            : new RefundResult("re_answered", RefundStatus.Succeeded);
        Clock.Advance(PastStuck);

        Assert.Equal(1, await ReconcileAsync());
        Assert.Equal(answered.RefundOrderId, Assert.IsType<RecordRefundOutcome>(Assert.Single(Bus.Invoked)).RefundOrderId);
        Assert.Contains(Logs.Lines, line => line.StartsWith("Warning") && line.Contains(unreachable.RefundOrderId.ToString()));
    }
}
