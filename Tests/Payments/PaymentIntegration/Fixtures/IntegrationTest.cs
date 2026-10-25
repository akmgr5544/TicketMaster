using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentIntegration.Fixtures;

[Collection(PaymentsCollection.Name)]
public abstract class IntegrationTest(PaymentsFixture fixture) : IAsyncLifetime
{
    public const string Usd = "USD";

    private AsyncServiceScope _act;

    protected ControllableTimeProvider Clock => fixture.Clock;

    protected StubPsp Psp => fixture.Psp;

    protected MessageBusLog Bus => fixture.Bus;

    protected LogCapture Logs => fixture.Logs;

    protected ISender Sender => _act.ServiceProvider.GetRequiredService<ISender>();

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _act = fixture.Services.CreateAsyncScope();
    }

    public async Task DisposeAsync() => await _act.DisposeAsync();

    // Reading back through the scope that wrote returns the tracked instance and proves nothing about what reached
    // the database.
    protected async Task<T> ReadAsync<T>(Func<PaymentDbContext, Task<T>> read)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<PaymentDbContext>());
    }

    protected Task<PaymentEvent> ReadCheckoutAsync(long bookingId) =>
        ReadAsync(c => c.PaymentEvents.SingleAsync(e => e.BookingId == bookingId));

    // A scope of its own: a second writer, a redelivered message, or seeding that must not prime the act scope.
    protected async Task InScopeAsync(Func<PaymentDbContext, Task> work)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await work(scope.ServiceProvider.GetRequiredService<PaymentDbContext>());
    }

    // Runs a Wolverine handler the way Wolverine's EF Core middleware does: built from the message's own scope,
    // handed that scope's PaymentDbContext, and its changes saved once it returns. The transaction around it and the
    // outbox are Wolverine's, and are proven against the real host in Mechanics/.
    protected async Task<TResult> HandleAsync<THandler, TResult>(
        Func<THandler, PaymentDbContext, IServiceProvider, Task<TResult>> handle) where THandler : notnull
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var result = await handle(handler, context, scope.ServiceProvider);
        await context.SaveChangesAsync();
        return result;
    }

    protected Task HandleAsync<THandler>(Func<THandler, PaymentDbContext, IServiceProvider, Task> handle)
        where THandler : notnull =>
        HandleAsync<THandler, bool>(async (handler, context, services) =>
        {
            await handle(handler, context, services);
            return true;
        });

    protected AsyncServiceScope NewScope() => fixture.Services.CreateAsyncScope();

    protected static long NewBookingId() => Random.Shared.NextInt64(1, long.MaxValue);

    // A checkout with one order, driven to the requested state through the root, the way production reaches it.
    // A Success order is settled too (both flags set) unless told otherwise, since production never leaves one
    // unsettled; its wallet and ledger are not written — a test that needs them seeds them.
    protected async Task<PaymentEvent> SeedCheckoutAsync(
        OrderState state = OrderState.Init,
        decimal amount = 100m,
        string currency = Usd,
        Guid? buyerId = null,
        Guid? sellerId = null,
        long? bookingId = null,
        string provider = "Stripe",
        bool settled = true)
    {
        var checkout = PaymentEvent.Create(bookingId ?? NewBookingId(), buyerId ?? Guid.NewGuid(), currency);
        checkout.AddPaymentOrder(sellerId ?? Guid.NewGuid(), amount);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        if (state != OrderState.Init)
            checkout.StartOrder(orderId, provider, StubGateway.ReferenceFor(orderId));
        if (state == OrderState.Success)
        {
            checkout.SucceedOrder(orderId, StubGateway.ReferenceFor(orderId));
            if (settled)
                checkout.MarkOrderSettled(orderId);
        }
        if (state == OrderState.Failed)
            checkout.FailOrder(orderId);

        await InScopeAsync(async c =>
        {
            c.PaymentEvents.Add(checkout);
            await c.SaveChangesAsync();
        });
        return checkout;
    }

    // A checkout with one order per entry, each for its own seller, driven like SeedCheckoutAsync.
    protected async Task<PaymentEvent> SeedCheckoutWithOrdersAsync(Guid buyerId, long bookingId,
        params (OrderState State, decimal Amount)[] orders)
    {
        var checkout = PaymentEvent.Create(bookingId, buyerId, Usd);
        foreach (var (_, amount) in orders)
            checkout.AddPaymentOrder(Guid.NewGuid(), amount);

        var created = checkout.PaymentOrders.ToArray();
        for (var i = 0; i < orders.Length; i++)
        {
            var orderId = created[i].PaymentOrderId;
            if (orders[i].State != OrderState.Init)
                checkout.StartOrder(orderId, "Stripe", StubGateway.ReferenceFor(orderId));
            if (orders[i].State == OrderState.Success)
            {
                checkout.SucceedOrder(orderId, StubGateway.ReferenceFor(orderId));
                checkout.MarkOrderSettled(orderId);
            }
            if (orders[i].State == OrderState.Failed)
                checkout.FailOrder(orderId);
        }

        await InScopeAsync(async c =>
        {
            c.PaymentEvents.Add(checkout);
            await c.SaveChangesAsync();
        });
        return checkout;
    }

    // A refund the PSP has already accepted, as RecordRefundOutcome leaves it (without the money it moved).
    protected async Task<RefundOrder> SeedSettledRefundAsync(Guid paymentOrderId, decimal amount, Guid? requestId = null)
    {
        var refund = await SeedRefundAsync(paymentOrderId, amount, requestId);
        await InScopeAsync(async c =>
        {
            var stored = await c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId);
            stored.MarkAsSettled($"re_{refund.RefundOrderId:N}");
            await c.SaveChangesAsync();
        });
        return refund;
    }

    protected async Task<Wallet> SeedWalletAsync(Guid userId, decimal balance = 0m, string currency = Usd)
    {
        var wallet = new Wallet(userId, currency);
        if (balance > 0)
            wallet.CreditFunds(Money.Create(balance, currency));
        await InScopeAsync(async c =>
        {
            c.Wallets.Add(wallet);
            await c.SaveChangesAsync();
        });
        return wallet;
    }

    protected async Task<RefundOrder> SeedRefundAsync(Guid paymentOrderId, decimal amount, Guid? requestId = null,
        string currency = Usd, string reason = "Seeded")
    {
        var refund = RefundOrder.Create(paymentOrderId, requestId ?? Guid.NewGuid(), Money.Create(amount, currency), reason);
        await InScopeAsync(async c =>
        {
            c.RefundOrders.Add(refund);
            await c.SaveChangesAsync();
        });
        return refund;
    }

    protected async Task<PaymentMethod> SeedPaymentMethodAsync(Guid userId, bool isDefault = false,
        string pspName = "Stripe", string pspToken = "pm_seeded")
    {
        var method = PaymentMethod.Create(userId.ToString(), pspName, pspToken);
        if (isDefault)
            method.MarkAsDefault();
        await InScopeAsync(async c =>
        {
            c.PaymentMethods.Add(method);
            await c.SaveChangesAsync();
        });
        return method;
    }
}

public enum OrderState
{
    Init,
    Executing,
    Success,
    Failed
}
