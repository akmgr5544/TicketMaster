using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Fixtures;

public abstract class QueryTest(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    // Sent from a scope of its own, and that scope's context is checked afterwards: a read that leaves an
    // entity tracked loaded the entity instead of projecting it.
    protected async Task<Result<T>> SendAsync<T>(IRequest<Result<T>> query)
    {
        await using var scope = NewScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(query);

        var tracked = scope.ServiceProvider.GetRequiredService<PaymentDbContext>().ChangeTracker.Entries().Count();
        Assert.True(tracked == 0, $"The query left {tracked} tracked entities behind.");
        return result;
    }

    // The fixture's seeder picks a random buyer and random merchants; these tests need to choose them.
    protected async Task<PaymentEvent> SeedCheckoutForAsync(Guid buyerId, params OrderLine[] lines)
    {
        var checkout = CheckoutSeed.Create(Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), buyerId, lines);
        await InScopeAsync(async c =>
        {
            c.PaymentEvents.Add(checkout);
            await c.SaveChangesAsync();
        });
        Events.Reset();
        return checkout;
    }

    // Drives one order to a fully settled state — succeeded, wallet and ledger flags set — and writes its
    // ledger pair, the way a settlement would.
    protected async Task SettleAsync(PaymentEvent checkout, Guid paymentOrderId)
    {
        await InScopeAsync(async c =>
        {
            var loaded = await c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId);
            CheckoutSeed.Drive(loaded, paymentOrderId, OrderState.Success);
            loaded.MarkWalletUpdated(paymentOrderId);
            loaded.MarkLedgerUpdated(paymentOrderId);
            c.LedgerEntries.AddRange(LedgerEntry.RecordPayIn(loaded.Order(paymentOrderId)));
            await c.SaveChangesAsync();
        });
        Events.Reset();
    }
}
