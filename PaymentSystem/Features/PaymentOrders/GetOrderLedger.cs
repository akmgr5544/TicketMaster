using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentOrders;

public static class GetOrderLedger
{
    public sealed record Query(Guid CallerId, Guid PaymentOrderId) : IRequest<Result<Response>>;

    // SignedSum counts a debit as positive and a credit as negative; a balanced ledger reads 0.
    public sealed record Response(Guid PaymentOrderId, decimal SignedSum, IReadOnlyList<Entry> Entries);

    public sealed record Entry(Guid AccountId, string Type, decimal Amount, string Currency, DateTime CreatedAt,
        string Reason);

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Checked separately from the entries: a visible order not yet settled has an empty ledger,
            // which must read as 200 with no entries rather than as not found.
            var visible = await context.Set<PaymentOrder>()
                .AnyAsync(o => o.PaymentOrderId == request.PaymentOrderId
                               && (o.BuyerId == request.CallerId || o.MerchantId == request.CallerId),
                    cancellationToken);
            if (!visible)
                return Error.NotFound("payment_order_not_found", $"Payment order {request.PaymentOrderId} was not found.");

            // Not paginated: the unique (PaymentOrderId, Reason, Type) index allows at most a pay-in pair and a
            // refund pair per order.
            var rows = await context.LedgerEntries
                .Where(e => e.PaymentOrderId == request.PaymentOrderId)
                .OrderBy(e => e.Reason).ThenBy(e => e.Type)
                .Select(e => new { e.AccountId, e.Type, e.Reason, e.Amount, e.Currency, e.CreatedAt })
                .ToListAsync(cancellationToken);

            var entries = rows
                .Select(e => new Entry(e.AccountId, e.Type.ToString(), e.Amount, e.Currency, e.CreatedAt, e.Reason.ToString()))
                .ToList();
            var signedSum = rows.Sum(e => e.Type == EntryType.Debit ? e.Amount : -e.Amount);

            return new Response(request.PaymentOrderId, signedSum, entries);
        }
    }
}

public sealed class GetOrderLedgerEndpoints : IEndpointMarker
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
