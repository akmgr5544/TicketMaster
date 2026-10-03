using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Pipelines;

namespace PaymentSystem.Features.PaymentOrders;

// The write half of any PSP answer obtained outside a transaction — a synchronous charge, or a reconciliation
// lookup. In a transaction of its own: the order's outcome, and through it the settlement, the ledger and
// BookingPaid, commit or roll back together, without the PSP call inside.
internal static class RecordOutcome
{
    public sealed record Command(Guid PaymentOrderId, PaymentStatus Status)
        : IRequest<Response>, ITransactionalRequest;

    // NotRecorded: the provider has the money but the order could not take it — it settled the other way, or the
    // write lost to concurrent writers twice. It needs reconciling; the buyer must not simply pay again.
    public enum Recorded
    {
        Applied,
        Unchanged,
        NotRecorded
    }

    public sealed record Response(Recorded Kind, PaymentOrderStatus? OrderStatus);

    internal sealed class Handler(PaymentDbContext context, ILogger<Handler> logger) : IRequestHandler<Command, Response>
    {
        public async Task<Response> Handle(Command request, CancellationToken cancellationToken)
        {
            // Only Succeeded and Canceled are final; Failed included, the customer can still retry.
            if (request.Status is not (PaymentStatus.Succeeded or PaymentStatus.Canceled))
                return new Response(Recorded.Unchanged, null);

            for (var attempt = 0;; attempt++)
            {
                var checkout = await context.PaymentEvents
                    .Where(e => e.PaymentOrders.Any(o => o.PaymentOrderId == request.PaymentOrderId))
                    .SingleOrDefaultAsync(cancellationToken);
                if (checkout is null)
                    return new Response(Recorded.Unchanged, null);

                var update = checkout.ApplyProviderAnswer(request.PaymentOrderId, request.Status == PaymentStatus.Succeeded);
                var order = checkout.Order(request.PaymentOrderId);
                switch (update)
                {
                    case OrderUpdate.Superseded:
                        logger.LogWarning(
                            "Payment order {PaymentOrderId} is {OrderStatus} but the provider reports {ProviderStatus}; it needs reconciling.",
                            request.PaymentOrderId, order.Status, request.Status);
                        return new Response(Recorded.NotRecorded, order.Status);
                    case OrderUpdate.AlreadyApplied or OrderUpdate.NotStarted:
                        return new Response(Recorded.Unchanged, order.Status);
                }

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return new Response(Recorded.Applied, order.Status);
                }
                // Events still pending means this write itself was refused, and it is retried once from a cleared
                // tracker. Once they are cleared the refusal came from a settlement handler's own save inside the
                // dispatch, after this write landed — retrying would call it a redelivery and drop the settlement.
                catch (DbUpdateConcurrencyException) when (checkout.DomainEvents.Length > 0 && attempt == 0)
                {
                    context.ChangeTracker.Clear();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return new Response(Recorded.NotRecorded, null);
                }
            }
        }
    }
}
