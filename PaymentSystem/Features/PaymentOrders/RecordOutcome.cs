using MediatR;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Psp;

namespace PaymentSystem.Features.PaymentOrders;

// The write half of any PSP answer obtained outside a transaction — a synchronous charge, or a reconciliation
// lookup. In a transaction of its own: the order's outcome, and through it the settlement, the ledger and
// BookingPaid, commit or roll back together, without the PSP call inside.
internal static class RecordOutcome
{
    public sealed record Command(Guid PaymentOrderId, PaymentStatus Status)
        : IRequest<ProviderOutcome.Applied>, ITransactionalRequest;

    internal sealed class Handler(PaymentDbContext context, ILogger<Handler> logger)
        : IRequestHandler<Command, ProviderOutcome.Applied>
    {
        public Task<ProviderOutcome.Applied> Handle(Command request, CancellationToken cancellationToken) =>
            ProviderOutcome.ApplyAsync(context, logger, request.PaymentOrderId, request.Status, cancellationToken);
    }
}
