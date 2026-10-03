using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;

namespace PaymentIntegration.Concurrency;

public sealed class VersionTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task NewCheckout_PersistsVersionZero()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted);

        Assert.Equal(0, (await ReadCheckoutAsync(saved.CheckoutId)).Version);
    }

    [Fact]
    public async Task EachRealChange_InItsOwnSave_IncrementsPersistedVersionByExactlyOne()
    {
        var saved = await SeedCheckoutAsync(OrderState.NotStarted);
        var id = saved.OrderId(0);
        var steps = new Action<PaymentSystem.Domain.PaymentEvent>[]
        {
            c => c.StartExecuting(id, CheckoutSeed.Provider, "tok"),
            // The success event would run the real Settle handler, whose own save is two more changes; the
            // wallet and ledger flags are driven as separate steps instead, so each save is one change.
            c =>
            {
                c.SucceedOrder(id);
                c.ClearDomainEvents();
            },
            c => c.MarkWalletUpdated(id),
            c => c.MarkLedgerUpdated(id)
        };

        for (var i = 0; i < steps.Length; i++)
        {
            await InScopeAsync(async c =>
            {
                steps[i](await c.PaymentEvents.SingleAsync());
                await c.SaveChangesAsync();
            });
            Assert.Equal(i + 1, (await ReadCheckoutAsync(saved.CheckoutId)).Version);
        }
    }

    [Fact]
    public async Task FailOrder_IncrementsVersion()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var before = (await ReadCheckoutAsync(saved.CheckoutId)).Version;

        (await LoadCheckoutAsync(saved.CheckoutId)).FailOrder(saved.OrderId(0));
        await Context.SaveChangesAsync();

        Assert.Equal(before + 1, (await ReadCheckoutAsync(saved.CheckoutId)).Version);
    }

    [Fact]
    public async Task RefusedTransition_LeavesVersionUntouched()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        var before = (await ReadCheckoutAsync(saved.CheckoutId)).Version;

        var checkout = await LoadCheckoutAsync(saved.CheckoutId);
        Assert.Throws<PaymentSystem.Domain.Exceptions.PaymentDomainException>(() => checkout.FailOrder(saved.OrderId(0)));
        Assert.Throws<PaymentSystem.Domain.Exceptions.PaymentDomainException>(() => checkout.StartExecuting(saved.OrderId(0), CheckoutSeed.Provider, "tok"));
        Assert.Throws<PaymentSystem.Domain.Exceptions.PaymentDomainException>(() => checkout.SucceedOrder(Guid.NewGuid()));

        Assert.Equal(0, await Context.SaveChangesAsync());
        Assert.Equal(before, (await ReadCheckoutAsync(saved.CheckoutId)).Version);
    }
}
