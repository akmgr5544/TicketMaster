using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Psp;

namespace PaymentSystem.Features.PaymentOrders;

// Asks the provider about orders that have sat Executing too long: a charge whose outcome failed to record, or a
// webhook that never came. Without it such an order waits for the 15-minute expiry, which fails it over money the
// provider may already have taken.
public static class ReconcileOrders
{
    public sealed record Query(DateTime UpdatedBefore, int Limit) : IRequest<IReadOnlyList<StaleOrder>>;

    public sealed record StaleOrder(Guid PaymentOrderId, string? Provider, string? PspToken);

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Query, IReadOnlyList<StaleOrder>>
    {
        public async Task<IReadOnlyList<StaleOrder>> Handle(Query request, CancellationToken cancellationToken) =>
            await context.Set<PaymentOrder>().AsNoTracking()
                .Where(o => o.Status == PaymentOrderStatus.Executing && o.UpdatedAt < request.UpdatedBefore)
                .OrderBy(o => o.UpdatedAt)
                .Take(request.Limit)
                .Select(o => new StaleOrder(o.PaymentOrderId, o.Provider, o.PspToken))
                .ToListAsync(cancellationToken);
    }
}

// Every instance runs it; that is safe because recording an outcome already reached is a no-op, and two instances
// recording at once settle through the checkout's version check like any other pair of writers.
public sealed class ReconcileOrdersJob(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ReconcileOrdersJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Long enough that a buyer still on the provider's page is not asked about on every pass, and short enough that
    // a lost outcome is found well inside the checkout's 15-minute window.
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Reconciling stale payment orders failed; the next pass will try again.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ReconcileOrders.StaleOrder> stale;
        await using (var scope = scopes.CreateAsyncScope())
        {
            stale = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new ReconcileOrders.Query(clock.GetUtcNow().UtcDateTime - StaleAfter, BatchSize), cancellationToken);
        }

        foreach (var order in stale)
            await ReconcileAsync(order, cancellationToken);
    }

    // A scope per order: each outcome is its own unit of work, and one order's failure leaves the rest untouched.
    private async Task ReconcileAsync(ReconcileOrders.StaleOrder order, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var gateways = scope.ServiceProvider.GetRequiredService<IPaymentGatewayFactory>();

        PaymentResult? payment;
        try
        {
            payment = await ProviderOutcome.GatewayFor(gateways, order.Provider)
                .LookupAsync(new PaymentLookupRequest(order.PaymentOrderId, order.PspToken), cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            logger.LogWarning(exception, "Could not look up payment order {PaymentOrderId}; it stays Executing.",
                order.PaymentOrderId);
            return;
        }

        if (payment is null)
            return;

        var applied = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new RecordOutcome.Command(order.PaymentOrderId, payment.Status), cancellationToken);
        if (applied.Kind == ProviderOutcome.Kind.Applied)
            logger.LogInformation("Reconciled payment order {PaymentOrderId} as {Status}.", order.PaymentOrderId,
                applied.OrderStatus);
    }
}
