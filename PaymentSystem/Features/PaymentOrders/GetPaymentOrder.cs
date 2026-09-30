using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentOrders;

public static class GetPaymentOrder
{
    public sealed record Query(Guid CallerId, Guid PaymentOrderId) : IRequest<Result<Response>>;

    // No PspToken: it is the PSP's handle on the buyer's payment method and never leaves this service.
    public sealed record Response(
        Guid PaymentOrderId,
        Guid CheckoutId,
        Guid BuyerId,
        Guid MerchantId,
        string ViewerRole,
        decimal Amount,
        string Currency,
        string Status,
        bool WalletUpdated,
        bool LedgerUpdated,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public const string BuyerRole = "Buyer";
    public const string MerchantRole = "Merchant";

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Both parties are in the predicate: to anyone else the order does not exist.
            var order = await context.Set<PaymentOrder>()
                .Where(o => o.PaymentOrderId == request.PaymentOrderId
                            && (o.BuyerId == request.CallerId || o.MerchantId == request.CallerId))
                .Select(o => new Response(
                    o.PaymentOrderId,
                    o.CheckoutId,
                    o.BuyerId,
                    o.MerchantId,
                    // A seller buying their own ticket is answered as the buyer.
                    o.BuyerId == request.CallerId ? BuyerRole : MerchantRole,
                    o.Amount,
                    o.Currency,
                    o.Status.ToString(),
                    o.WalletUpdated,
                    o.LedgerUpdated,
                    o.CreatedAt,
                    o.UpdatedAt))
                .SingleOrDefaultAsync(cancellationToken);

            if (order is null)
                return Error.NotFound("payment_order_not_found", $"Payment order {request.PaymentOrderId} was not found.");

            return order;
        }
    }
}

public sealed class GetPaymentOrderEndpoints : IEndpointMarker
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
