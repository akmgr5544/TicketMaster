using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.RefundOrders;

public static class GetOrderRefunds
{
    public sealed record Query(Guid CallerId, Guid PaymentOrderId) : IRequest<Result<IReadOnlyList<Response>>>;

    public sealed record Response(
        Guid RefundOrderId,
        decimal RefundAmount,
        string Currency,
        string Status,
        string RefundReason,
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

            var refunds = await context.RefundOrders
                .Where(r => r.PaymentOrderId == request.PaymentOrderId)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new Response(r.RefundOrderId, r.RefundAmount, r.Currency, r.Status, r.RefundReason, r.CreatedAt))
                .ToListAsync(cancellationToken);

            return refunds;
        }
    }
}

public sealed class GetOrderRefundsEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/payments/orders/{paymentOrderId:guid}/refunds",
            async (Guid paymentOrderId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new GetOrderRefunds.Query(callerId, paymentOrderId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
