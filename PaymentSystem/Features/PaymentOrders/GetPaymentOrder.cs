using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentOrders;

public static class GetPaymentOrder
{
    public sealed record Query(Guid CallerId, Guid PaymentOrderId) : IRequest<Result<Response>>;

    public sealed record Response(
        Guid PaymentOrderId,
        Guid CheckoutId,
        Guid SellerId,
        decimal Amount,
        string Currency,
        string Status,
        bool WalletUpdated,
        bool LedgerUpdated,
        DateTime CreatedAt);

    internal sealed class Handler(PaymentQueryDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Visible to its seller and to the buyer of its checkout; to anyone else it does not exist.
            var order = await context.PaymentOrders
                .Where(o => o.PaymentOrderId == request.PaymentOrderId
                            && (o.SellerId == request.CallerId
                                || context.PaymentEvents.Any(e => e.CheckoutId == o.CheckoutId && e.BuyerId == request.CallerId)))
                .Select(o => new Response(
                    o.PaymentOrderId,
                    o.CheckoutId,
                    o.SellerId,
                    o.Amount,
                    o.Currency,
                    o.Status,
                    o.WalletUpdated,
                    o.LedgerUpdated,
                    o.CreatedAt))
                .SingleOrDefaultAsync(cancellationToken);

            if (order is null)
                return Error.NotFound("payment_order_not_found", $"Payment order {request.PaymentOrderId} was not found.");

            return order;
        }
    }
}

public sealed class GetPaymentOrderEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/payments/orders/{paymentOrderId:guid}",
            async (Guid paymentOrderId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new GetPaymentOrder.Query(callerId, paymentOrderId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
