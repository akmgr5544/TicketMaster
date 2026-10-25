using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain.Shared;

namespace PaymentIntegration.Persistence;

// The row version (xmin) is what turns a PSP answer racing the expiry, or two settlements crediting one seller, into a
// conflict the message retry resolves — instead of the later writer silently overwriting the earlier one.
public sealed class ConcurrencyTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
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
}
