using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Persistence;

// A checkout's Version, and a wallet's row version (xmin), turn two writers into a conflict the message retry resolves —
// instead of the later writer silently overwriting the earlier one.
public sealed class ConcurrencyTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    // A PSP answer racing the expiry: without a conflict the later writer would overwrite the earlier one's decision.
    [Fact]
    public async Task Two_writers_changing_one_order_cannot_both_win()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        await using var first = NewScope();
        await using var second = NewScope();
        var firstContext = first.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var secondContext = second.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var paid = await firstContext.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId);
        var expired = await secondContext.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId);

        paid.SucceedOrder(orderId, "pi_paid");
        expired.Cancel();
        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_credits_to_one_wallet_cannot_both_win()
    {
        var wallet = await SeedWalletAsync(Guid.NewGuid(), 10m);

        await using var first = NewScope();
        await using var second = NewScope();
        var firstContext = first.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var secondContext = second.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var one = await firstContext.Wallets.SingleAsync(w => w.WalletId == wallet.WalletId);
        var two = await secondContext.Wallets.SingleAsync(w => w.WalletId == wallet.WalletId);

        one.CreditFunds(Money.Create(5m, Usd));
        two.CreditFunds(Money.Create(7m, Usd));
        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
        Assert.Equal(15m, (await ReadAsync(c => c.Wallets.SingleAsync(w => w.WalletId == wallet.WalletId))).CurrentBalance.Amount);
    }

    [Fact]
    public async Task Two_answers_to_one_refund_cannot_both_win()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var refund = await SeedRefundAsync(checkout.PaymentOrders.Single().PaymentOrderId, 100m);

        await using var first = NewScope();
        await using var second = NewScope();
        var firstContext = first.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var secondContext = second.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var succeeded = await firstContext.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId);
        var failed = await secondContext.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId);

        succeeded.MarkAsSettled("re_1");
        failed.MarkAsFailed();
        await firstContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
        Assert.Equal(RefundOrderStatus.Succeeded,
            (await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId))).Status);
    }

    // A webhook and the refund reconciler recording the same final answer at once: one wins, and the loser's retry finds
    // the refund answered, so the money moves and Bookings hears about it once.
    [Fact]
    public async Task A_refund_answered_twice_at_once_is_settled_once()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var order = checkout.PaymentOrders.Single();
        await SeedWalletAsync(order.SellerId, 100m);
        var refund = await SeedRefundAsync(order.PaymentOrderId, 100m);
        var answer = new RecordRefundOutcome(refund.RefundOrderId, true, "re_1", null);

        await using var webhook = NewScope();
        await using var reconciler = NewScope();
        var webhookContext = webhook.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var reconcilerContext = reconciler.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var fromWebhook = await ActivatorUtilities.CreateInstance<RecordRefundOutcomeHandler>(webhook.ServiceProvider)
            .Handle(answer, webhookContext, CancellationToken.None);
        var fromReconciler = await ActivatorUtilities.CreateInstance<RecordRefundOutcomeHandler>(reconciler.ServiceProvider)
            .Handle(answer, reconcilerContext, CancellationToken.None);
        await webhookContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => reconcilerContext.SaveChangesAsync());
        var retried = await HandleAsync<RecordRefundOutcomeHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(answer, context, CancellationToken.None));

        Assert.Single(fromWebhook.OfType<BookingRefundedIntegrationEvent>());
        Assert.Single(fromReconciler.OfType<BookingRefundedIntegrationEvent>());
        Assert.Empty(retried);
        Assert.Equal(0m, (await ReadAsync(c => c.Wallets.SingleAsync(w => w.UserId == order.SellerId))).CurrentBalance.Amount);
        Assert.Equal(2, await ReadAsync(c => c.LedgerEntries.CountAsync(l => l.TransactionRefId == refund.RefundOrderId)));
    }
}
