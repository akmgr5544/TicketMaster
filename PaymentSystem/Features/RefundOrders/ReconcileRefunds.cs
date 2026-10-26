using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using Wolverine;

namespace PaymentSystem.Features.RefundOrders;

// A refund the PSP accepted without finishing is final only when the PSP says so. Stripe announces it by webhook, which
// can be lost; Braintree never pushes a card refund. So a refund still processing a while later is asked about
// directly, and a final answer is recorded exactly as a webhook's would be.
public sealed class RefundReconciler(
    PaymentQueryDbContext context,
    IPaymentGatewayFactory gateways,
    IMessageBus bus,
    TimeProvider clock,
    ILogger<RefundReconciler> logger)
{
    // Long enough that a webhook on its way normally lands first.
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(2);

    // Returns how many refunds it recorded an outcome for.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - StuckAfter;

        var processing = await context.RefundOrders
            .Where(r => r.Status == nameof(RefundOrderStatus.Processing) && r.ProcessingSince < cutoff)
            .Join(context.PaymentOrders, r => r.PaymentOrderId, o => o.PaymentOrderId,
                (r, o) => new { r.RefundOrderId, r.PaymentOrderId, r.PspRefundReference, r.ProcessingSince, o.Provider })
            .OrderBy(r => r.ProcessingSince)
            .ToListAsync(cancellationToken);

        var recorded = 0;
        foreach (var refund in processing)
        {
            if (string.IsNullOrEmpty(refund.PspRefundReference))
                continue;

            RefundResult? result;
            try
            {
                result = await gateways.ForProvider(refund.Provider)
                    .LookupRefundAsync(new RefundLookupRequest(refund.PaymentOrderId, refund.PspRefundReference), cancellationToken);
            }
            // One refund's provider being unreachable must not stop the others; it is asked again next run.
            catch (PaymentProviderException exception)
            {
                logger.LogWarning(exception, "Could not reconcile refund {RefundOrderId}.", refund.RefundOrderId);
                continue;
            }

            if (result?.Status is not (RefundStatus.Succeeded or RefundStatus.Failed))
                continue;

            await bus.InvokeAsync(
                new RecordRefundOutcome(refund.RefundOrderId, result.Status == RefundStatus.Succeeded,
                    result.ProviderReference, result.FailureReason),
                cancellationToken);
            recorded++;
        }

        return recorded;
    }
}

public sealed class ReconcileRefundsJob(IServiceScopeFactory scopes, ILogger<ReconcileRefundsJob> logger) : BackgroundService
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
                await scope.ServiceProvider.GetRequiredService<RefundReconciler>().RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Reconciling refunds failed; it runs again in {Every}.", Every);
            }
        }
    }
}
