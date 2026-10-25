using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using Wolverine;

namespace PaymentSystem.Features.PaymentOrders;

// A webhook can be lost, and a buyer can leave the PSP's page mid-payment. An order still executing a while after the
// buyer was sent to the PSP is asked about directly, and a final answer is recorded exactly as a webhook's would be.
public sealed class OrderReconciler(
    PaymentQueryDbContext context,
    IPaymentGatewayFactory gateways,
    IMessageBus bus,
    TimeProvider clock,
    ILogger<OrderReconciler> logger)
{
    // Long enough that a webhook on its way normally lands first.
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(2);

    // Returns how many orders it recorded an outcome for.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - StuckAfter;

        var stuck = await context.PaymentOrders
            .Where(o => o.Status == nameof(PaymentOrderStatus.Executing) && o.ExecutingSince < cutoff)
            .OrderBy(o => o.ExecutingSince)
            .ToListAsync(cancellationToken);

        var recorded = 0;
        foreach (var order in stuck)
        {
            PaymentResult? result;
            try
            {
                result = await gateways.ForProvider(order.Provider)
                    .LookupAsync(new PaymentLookupRequest(order.PaymentOrderId, order.PspTokenRef), cancellationToken);
            }
            // One order's provider being unreachable must not stop the others; it is asked again next run.
            catch (PaymentProviderException exception)
            {
                logger.LogWarning(exception, "Could not reconcile payment order {PaymentOrderId}.", order.PaymentOrderId);
                continue;
            }

            // Not final yet (or nothing at the PSP): the expiry fails it if the buyer never finishes.
            if (result?.Status is not (PaymentStatus.Succeeded or PaymentStatus.Canceled))
                continue;

            await bus.InvokeAsync(
                new RecordPaymentOutcome(order.PaymentOrderId, result.Status == PaymentStatus.Succeeded, result.ProviderReference),
                cancellationToken);
            recorded++;
        }

        return recorded;
    }
}

public sealed class ReconcileOrdersJob(IServiceScopeFactory scopes, ILogger<ReconcileOrdersJob> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OrderReconciler>().RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Reconciling payment orders failed; it runs again in {Every}.", Every);
            }
        }
    }
}
