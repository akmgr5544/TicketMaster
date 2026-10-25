using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.LedgerEntries;

// Read only: the ledger is append-only, and a mistake is corrected by a new reversing entry, never by an edit.
public static class GetOrderLedger
{
    public sealed record Query(Guid CallerId, Guid PaymentOrderId) : IRequest<Result<IReadOnlyList<Response>>>;

    public sealed record Response(
        long EntryId,
        Guid TransactionRefId,
        string RefType,
        string AccountId,
        string EntryType,
        decimal Amount,
        string Currency,
        DateTime CreatedAt);

    internal sealed class Handler(PaymentQueryDbContext context) : IRequestHandler<Query, Result<IReadOnlyList<Response>>>
    {
        public async Task<Result<IReadOnlyList<Response>>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Same visibility as the order itself: its seller or its checkout's buyer.
            var canSeeOrder = await context.PaymentOrders
                .AnyAsync(o => o.PaymentOrderId == request.PaymentOrderId
                               && (o.SellerId == request.CallerId
                                   || context.PaymentEvents.Any(e => e.CheckoutId == o.CheckoutId && e.BuyerId == request.CallerId)),
                    cancellationToken);

            if (!canSeeOrder)
                return Error.NotFound("payment_order_not_found", $"Payment order {request.PaymentOrderId} was not found.");

            // An order's entries are its pay-in, referenced by the order id, and each refund, referenced by the refund id.
            var refundIds = context.RefundOrders
                .Where(r => r.PaymentOrderId == request.PaymentOrderId)
                .Select(r => r.RefundOrderId);

            var entries = await context.LedgerEntries
                .Where(l => l.TransactionRefId == request.PaymentOrderId || refundIds.Contains(l.TransactionRefId))
                .OrderBy(l => l.EntryId)
                .Select(l => new Response(l.EntryId, l.TransactionRefId, l.RefType, l.AccountId, l.EntryType, l.Amount, l.Currency, l.CreatedAt))
                .ToListAsync(cancellationToken);

            return entries;
        }
    }
}

public sealed class GetOrderLedgerEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/payments/orders/{paymentOrderId:guid}/ledger",
            async (Guid paymentOrderId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new GetOrderLedger.Query(callerId, paymentOrderId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
