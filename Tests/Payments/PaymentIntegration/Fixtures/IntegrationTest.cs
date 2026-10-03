using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Data;
using PaymentSystem.Domain;

namespace PaymentIntegration.Fixtures;

[Collection(PaymentsCollection.Name)]
public abstract class IntegrationTest(PaymentsFixture fixture) : IAsyncLifetime
{
    private AsyncServiceScope _act;

    protected ControllableTimeProvider Clock => fixture.Clock;

    protected DomainEventRecorder Events => fixture.Events;

    protected PaymentDbContext Context => _act.ServiceProvider.GetRequiredService<PaymentDbContext>();

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _act = fixture.Services.CreateAsyncScope();
    }

    public async Task DisposeAsync() => await _act.DisposeAsync();

    // Reading back through the scope that wrote returns the tracked instance and proves nothing about what
    // reached the database.
    protected async Task<T> ReadAsync<T>(Func<PaymentDbContext, Task<T>> read)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<PaymentDbContext>());
    }

    protected Task<PaymentEvent> ReadCheckoutAsync(Guid checkoutId) =>
        ReadAsync(c => c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkoutId));

    protected Task<PaymentOrder> ReadOrderAsync(Guid paymentOrderId) =>
        ReadAsync(c => c.Set<PaymentOrder>().SingleAsync(o => o.PaymentOrderId == paymentOrderId));

    // A scope of its own for a second writer: the other side of a race, or a redelivered message.
    protected async Task InScopeAsync(Func<PaymentDbContext, Task> work)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await work(scope.ServiceProvider.GetRequiredService<PaymentDbContext>());
    }

    protected AsyncServiceScope NewScope() => fixture.Services.CreateAsyncScope();

    // The provider a seeded order was started with, which is the one every later PSP call for it goes to.
    protected virtual string SeedProvider => CheckoutSeed.Provider;

    protected Task<PaymentEvent> LoadCheckoutAsync(Guid checkoutId) =>
        Context.PaymentEvents.SingleAsync(e => e.CheckoutId == checkoutId);

    // Seeds a checkout with one order per requested state, each driven there through the root, and clears
    // the event recorder so arrange-time events never reach the assertions.
    protected async Task<PaymentEvent> SeedCheckoutAsync(params OrderState[] states) =>
        await SeedCheckoutAsync(CheckoutSeed.Lines(states.Length == 0 ? 1 : states.Length), states);

    protected async Task<PaymentEvent> SeedCheckoutAsync(IReadOnlyCollection<OrderLine> lines, params OrderState[] states)
    {
        var checkout = CheckoutSeed.Create(Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(), lines);
        var orders = checkout.PaymentOrders.ToArray();
        for (var i = 0; i < states.Length; i++)
            CheckoutSeed.Drive(checkout, orders[i].PaymentOrderId, states[i], SeedProvider);
        // Seeds a state, not a settlement: a pending success event would run the real Settle handler on save
        // and write the wallet and ledger a test means to arrange itself.
        checkout.ClearDomainEvents();

        await InScopeAsync(async c =>
        {
            c.BookingClaims.Add(BookingClaim.Requested(checkout.BookingId));
            c.PaymentEvents.Add(checkout);
            await c.SaveChangesAsync();
        });
        Events.Reset();
        return checkout;
    }

    protected async Task<Wallet> SeedWalletAsync(string currency = "USD", decimal initialCredit = 0m)
    {
        var wallet = Wallet.Create(Guid.NewGuid(), currency);
        if (initialCredit > 0)
            wallet.Credit(initialCredit, currency);
        await InScopeAsync(async c => { c.Wallets.Add(wallet); await c.SaveChangesAsync(); });
        return wallet;
    }
}

public enum OrderState
{
    NotStarted,
    Executing,
    Success,
    Failed
}

public static class CheckoutSeed
{
    public const string Token = "psp_tok_123";

    public const string Provider = "Stripe";

    public static OrderLine[] Lines(int count, decimal amount = 25.50m, string currency = "USD") =>
        Enumerable.Range(0, count).Select(_ => new OrderLine(Guid.NewGuid(), amount, currency)).ToArray();

    public static PaymentEvent New(int orders = 1, decimal amount = 25.50m, string currency = "USD") =>
        Create(Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(), Lines(orders, amount, currency));

    public static PaymentEvent Create(Guid checkoutId, long bookingId, Guid buyerId, IEnumerable<OrderLine> lines)
    {
        var checkout = PaymentEvent.Create(checkoutId, bookingId, buyerId);
        foreach (var line in lines)
            checkout.AddOrder(line.MerchantId, line.Amount, line.Currency);
        return checkout;
    }

    public static void Drive(PaymentEvent checkout, Guid paymentOrderId, OrderState state, string provider = Provider)
    {
        if (state == OrderState.NotStarted)
            return;
        checkout.StartExecuting(paymentOrderId, provider, Token);
        if (state == OrderState.Success)
            checkout.SucceedOrder(paymentOrderId);
        else if (state == OrderState.Failed)
            checkout.FailOrder(paymentOrderId);
    }

    public static Guid OrderId(this PaymentEvent checkout, int index) =>
        checkout.PaymentOrders.ElementAt(index).PaymentOrderId;

    public static PaymentOrder Order(this PaymentEvent checkout, Guid paymentOrderId) =>
        checkout.PaymentOrders.Single(o => o.PaymentOrderId == paymentOrderId);
}

// One seller's order as a test describes it; expanded into PaymentEvent.AddOrder calls.
public sealed record OrderLine(Guid MerchantId, decimal Amount, string Currency);
