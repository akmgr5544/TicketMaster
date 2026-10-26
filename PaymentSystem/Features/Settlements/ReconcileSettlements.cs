using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;
using PaymentSystem.Query;
using PaymentSystem.Shared.Messaging;
using Wolverine;

namespace PaymentSystem.Features.Settlements;

// The last line of defence: what each PSP actually settled, compared with our books. Every difference is recorded for
// a person and nothing is corrected here — except a refund the PSP took back, which is its final word on that refund
// and goes through the same undo a webhook's would.
public sealed class SettlementReconciler(
    PaymentDbContext context,
    PaymentQueryDbContext queries,
    IPaymentGatewayFactory gateways,
    IMessageBus bus,
    TimeProvider clock,
    ILogger<SettlementReconciler> logger)
{
    // Card settlement lands one to three days after the payment; past five, a missing line is not just late.
    public static readonly TimeSpan Grace = TimeSpan.FromDays(5);

    // Grace plus the three days settlement can take, so every order old enough to be checked had all the days its
    // line could fall on read in this same run — nothing is flagged for a day that was simply never read.
    public static readonly TimeSpan Window = TimeSpan.FromDays(8);

    private static readonly string[] PaidStatuses = [nameof(PaymentOrderStatus.Success), nameof(PaymentOrderStatus.Refunded)];

    // Returns how many new discrepancies it recorded.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var to = now.Date;
        var from = to - Window;

        var found = 0;
        foreach (var kind in Enum.GetValues<PaymentProviderKind>())
        {
            if (Configured(kind) is not { } gateway)
                continue;

            IReadOnlyList<SettlementLine> lines;
            try
            {
                lines = await gateway.GetSettlementAsync(new SettlementRequest(from, to), cancellationToken);
            }
            // One provider being unreachable must not stop the other; the window is read again next run.
            catch (PaymentProviderException exception)
            {
                logger.LogWarning(exception, "Could not read {Provider}'s settlement.", kind);
                continue;
            }

            found += await ReconcileAsync(kind, lines, now, from, cancellationToken);
        }

        return found;
    }

    private IPaymentGateway? Configured(PaymentProviderKind kind)
    {
        try
        {
            return gateways.Get(kind);
        }
        catch (PaymentProviderException exception) when (exception.Kind == PaymentProviderErrorKind.Configuration)
        {
            return null;
        }
    }

    private async Task<int> ReconcileAsync(PaymentProviderKind kind, IReadOnlyList<SettlementLine> lines, DateTime now,
        DateTime from, CancellationToken cancellationToken)
    {
        var provider = kind.ToString();
        await RecordLinesAsync(provider, lines, now, cancellationToken);

        var findings = new List<SettlementDiscrepancy>();
        var refundsToFail = new List<(Guid RefundOrderId, string Reference)>();

        var orders = await OrdersByReferenceAsync(lines, cancellationToken);
        var refunds = await RefundsByReferenceAsync(lines, cancellationToken);
        var failedReferences = await context.SettlementRecords
            .Where(r => r.Provider == provider && r.Kind == SettlementEntryKind.RefundFailure)
            .Select(r => r.ProviderReference)
            .ToListAsync(cancellationToken);

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case SettlementLineKind.Charge:
                    CheckCharge(provider, line, orders.Find(o => o.PspTokenRef == line.ProviderReference)
                        ?? orders.Find(o => o.PaymentOrderId == line.PaymentOrderId), now, findings);
                    break;

                case SettlementLineKind.Refund:
                    CheckRefund(provider, line, FindRefund(refunds, line), failedReferences, now, findings);
                    break;

                case SettlementLineKind.RefundFailure when FindRefund(refunds, line) is { } refund:
                    // Already Failed: the webhook or the refund reconciler got there first.
                    if (refund.Status == nameof(RefundOrderStatus.Failed))
                        break;
                    if (refund.Status == nameof(RefundOrderStatus.Succeeded))
                        findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.RefundFailedAfterSuccess,
                            line.ProviderReference, "The PSP took back a refund it had reported done.", now,
                            refund.PaymentOrderId, refund.RefundOrderId, refund.RefundAmount, refund.Currency,
                            line.Amount, line.Currency));
                    refundsToFail.Add((refund.RefundOrderId, line.ProviderReference));
                    break;
            }
        }

        await FindUnsettledAsync(kind, provider, now, from, findings, cancellationToken);

        var recorded = await RecordFindingsAsync(provider, findings, cancellationToken);

        foreach (var (refundOrderId, reference) in refundsToFail)
            await bus.InvokeAsync(
                new RecordRefundOutcome(refundOrderId, false, reference, "Declined at settlement.", Final: true),
                cancellationToken);

        return recorded;
    }

    private async Task RecordLinesAsync(string provider, IReadOnlyList<SettlementLine> lines, DateTime now,
        CancellationToken cancellationToken)
    {
        var references = lines.Select(l => l.ProviderReference).Distinct().ToList();
        var known = (await context.SettlementRecords
                .Where(r => r.Provider == provider && references.Contains(r.ProviderReference))
                .Select(r => new { r.ProviderReference, r.Kind })
                .ToListAsync(cancellationToken))
            .Select(r => (r.ProviderReference, r.Kind))
            .ToHashSet();

        foreach (var line in lines)
        {
            var kind = ToEntryKind(line.Kind);
            if (!known.Add((line.ProviderReference, kind)))
                continue;

            context.SettlementRecords.Add(new SettlementRecord(provider, line.ProviderReference, kind,
                Money.Create(line.Amount, line.Currency), line.PaymentOrderId, line.RefundId, now));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private Task<List<PaymentOrderReadModel>> OrdersByReferenceAsync(IReadOnlyList<SettlementLine> lines,
        CancellationToken cancellationToken)
    {
        var charges = lines.Where(l => l.Kind == SettlementLineKind.Charge).ToList();
        var references = charges.Select(l => l.ProviderReference).ToList();
        var ids = charges.Where(l => l.PaymentOrderId is not null).Select(l => l.PaymentOrderId!.Value).ToList();

        return queries.PaymentOrders
            .Where(o => (o.PspTokenRef != null && references.Contains(o.PspTokenRef)) || ids.Contains(o.PaymentOrderId))
            .ToListAsync(cancellationToken);
    }

    private Task<List<RefundOrderReadModel>> RefundsByReferenceAsync(IReadOnlyList<SettlementLine> lines,
        CancellationToken cancellationToken)
    {
        var refundLines = lines.Where(l => l.Kind != SettlementLineKind.Charge).ToList();
        var references = refundLines.Select(l => l.ProviderReference).ToList();
        var requestIds = refundLines.Where(l => l.RefundId is not null).Select(l => l.RefundId!.Value).ToList();

        return queries.RefundOrders
            .Where(r => (r.PspRefundReference != null && references.Contains(r.PspRefundReference))
                        || requestIds.Contains(r.RequestId))
            .ToListAsync(cancellationToken);
    }

    // By the PSP's reference first; else by the refund id it was tagged with, which is the request id we sent.
    private static RefundOrderReadModel? FindRefund(List<RefundOrderReadModel> refunds, SettlementLine line) =>
        refunds.Find(r => r.PspRefundReference == line.ProviderReference)
        ?? refunds.Find(r => line.RefundId is { } refundId && r.RequestId == refundId
                             && (line.PaymentOrderId is null || r.PaymentOrderId == line.PaymentOrderId));

    private static void CheckCharge(string provider, SettlementLine line, PaymentOrderReadModel? order, DateTime now,
        List<SettlementDiscrepancy> findings)
    {
        if (order is null)
        {
            findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.UnknownCharge, line.ProviderReference,
                "The PSP settled a charge for no payment order of ours.", now,
                line.PaymentOrderId, actualAmount: line.Amount, actualCurrency: line.Currency));
            return;
        }

        if (!PaidStatuses.Contains(order.Status))
        {
            findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.ChargeForUnpaidOrder,
                line.ProviderReference, $"The PSP settled a charge for an order we count {order.Status}.", now,
                order.PaymentOrderId, null, order.Amount, order.Currency, line.Amount, line.Currency));
            return;
        }

        if (order.Amount != line.Amount || !SameCurrency(order.Currency, line.Currency))
            findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.ChargeAmountMismatch,
                line.ProviderReference, "The PSP settled a different amount than the order.", now,
                order.PaymentOrderId, null, order.Amount, order.Currency, line.Amount, line.Currency));
    }

    // Stripe writes a refund's line when the refund is created, still pending, and adds a separate failure line if it
    // fails; so a refund line beside our Processing refund is normal, and one beside our Failed refund is fine when
    // its failure line is there too.
    private static void CheckRefund(string provider, SettlementLine line, RefundOrderReadModel? refund,
        List<string> failedReferences, DateTime now, List<SettlementDiscrepancy> findings)
    {
        var givenBack = refund?.Status is nameof(RefundOrderStatus.Pending) or nameof(RefundOrderStatus.Processing)
            or nameof(RefundOrderStatus.Succeeded);
        if (refund is null || (!givenBack && !failedReferences.Contains(line.ProviderReference)))
        {
            findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.RefundNotRecorded,
                line.ProviderReference, "The PSP settled a refund we do not count as given back.", now,
                refund?.PaymentOrderId ?? line.PaymentOrderId, refund?.RefundOrderId,
                actualAmount: line.Amount, actualCurrency: line.Currency));
            return;
        }

        if (givenBack && (refund.RefundAmount != line.Amount || !SameCurrency(refund.Currency, line.Currency)))
            findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.RefundAmountMismatch,
                line.ProviderReference, "The PSP settled a different amount than the refund.", now,
                refund.PaymentOrderId, refund.RefundOrderId, refund.RefundAmount, refund.Currency, line.Amount, line.Currency));
    }

    // Only what this run's window fully covers: a payment started inside it and old enough that its line is overdue.
    private async Task FindUnsettledAsync(PaymentProviderKind kind, string provider, DateTime now, DateTime from,
        List<SettlementDiscrepancy> findings, CancellationToken cancellationToken)
    {
        var overdue = now - Grace;
        var isDefault = gateways.Default.Kind == kind;

        var orders = await queries.PaymentOrders
            .Where(o => (o.Provider == provider || (o.Provider == null && isDefault))
                        && PaidStatuses.Contains(o.Status)
                        && o.PspTokenRef != null
                        && o.ExecutingSince >= from && o.ExecutingSince < overdue)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(o => o.PaymentOrderId).ToList();

        var refunds = await queries.RefundOrders
            .Where(r => orderIds.Contains(r.PaymentOrderId) || r.CreatedAt >= from)
            .ToListAsync(cancellationToken);

        var settled = (await context.SettlementRecords
                .Where(r => r.Provider == provider)
                .Select(r => new { r.ProviderReference, r.Kind })
                .ToListAsync(cancellationToken))
            .Select(r => (r.ProviderReference, r.Kind))
            .ToHashSet();

        foreach (var order in orders)
        {
            // A sale refunded before it settled is voided, never settled; the void's reference is the sale's own.
            var voided = refunds.Any(r => r.PaymentOrderId == order.PaymentOrderId
                                          && r.Status == nameof(RefundOrderStatus.Succeeded)
                                          && r.PspRefundReference == order.PspTokenRef);
            if (voided || settled.Contains((order.PspTokenRef!, SettlementEntryKind.Charge)))
                continue;

            findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.OrderNotSettled, order.PspTokenRef!,
                $"We count the order paid since {order.ExecutingSince:u}, but the PSP has not settled it.", now,
                order.PaymentOrderId, null, order.Amount, order.Currency));
        }

        var overdueRefunds = refunds
            .Where(r => r.Status == nameof(RefundOrderStatus.Succeeded)
                        && r.CreatedAt >= from && r.SucceededAt < overdue
                        && r.PspRefundReference is not null)
            .ToList();
        var refundedOrderIds = overdueRefunds.Select(r => r.PaymentOrderId).Distinct().ToList();
        var paidReferences = await queries.PaymentOrders
            .Where(o => refundedOrderIds.Contains(o.PaymentOrderId)
                        && (o.Provider == provider || (o.Provider == null && isDefault)))
            .Select(o => new { o.PaymentOrderId, o.PspTokenRef })
            .ToDictionaryAsync(o => o.PaymentOrderId, o => o.PspTokenRef, cancellationToken);

        foreach (var refund in overdueRefunds)
        {
            if (!paidReferences.TryGetValue(refund.PaymentOrderId, out var saleReference)
                || refund.PspRefundReference == saleReference
                || settled.Contains((refund.PspRefundReference!, SettlementEntryKind.Refund)))
                continue;

            findings.Add(new SettlementDiscrepancy(provider, SettlementDiscrepancyKind.RefundNotSettled,
                refund.PspRefundReference!, $"We count the refund done since {refund.SucceededAt:u}, but the PSP has not settled it.",
                now, refund.PaymentOrderId, refund.RefundOrderId, refund.RefundAmount, refund.Currency));
        }
    }

    private async Task<int> RecordFindingsAsync(string provider, List<SettlementDiscrepancy> findings,
        CancellationToken cancellationToken)
    {
        if (findings.Count == 0)
            return 0;

        var references = findings.Select(f => f.Reference).Distinct().ToList();
        var known = (await context.SettlementDiscrepancies
                .Where(d => d.Provider == provider && references.Contains(d.Reference))
                .Select(d => new { d.Kind, d.Reference })
                .ToListAsync(cancellationToken))
            .Select(d => (d.Kind, d.Reference))
            .ToHashSet();

        var recorded = 0;
        foreach (var finding in findings)
        {
            if (!known.Add((finding.Kind, finding.Reference)))
                continue;

            context.SettlementDiscrepancies.Add(finding);
            recorded++;
            logger.LogError(
                "Settlement discrepancy {Kind} at {Provider} for {Reference}: {Detail} Expected {ExpectedAmount} {ExpectedCurrency}, " +
                "settled {ActualAmount} {ActualCurrency}. A person must resolve it.",
                finding.Kind, provider, finding.Reference, finding.Detail, finding.ExpectedAmount, finding.ExpectedCurrency,
                finding.ActualAmount, finding.ActualCurrency);
        }

        await context.SaveChangesAsync(cancellationToken);
        return recorded;
    }

    private static bool SameCurrency(string ours, string theirs) => string.Equals(ours, theirs, StringComparison.OrdinalIgnoreCase);

    private static SettlementEntryKind ToEntryKind(SettlementLineKind kind) => kind switch
    {
        SettlementLineKind.Charge => SettlementEntryKind.Charge,
        SettlementLineKind.Refund => SettlementEntryKind.Refund,
        _ => SettlementEntryKind.RefundFailure,
    };
}

public sealed class ReconcileSettlementsJob(IServiceScopeFactory scopes, ILogger<ReconcileSettlementsJob> logger)
    : BackgroundService
{
    // Each run reads the whole window, so running more often only repeats the same reads.
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SettlementReconciler>().RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Reconciling settlements failed; it runs again in {Every}.", Every);
            }
        }
    }
}
