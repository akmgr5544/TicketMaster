using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.Checkouts;

public static class GetCheckout
{
    public sealed record Query(Guid CallerId, long BookingId) : IRequest<Result<Response>>;

    public sealed record Response(
        Guid CheckoutId,
        long BookingId,
        Guid BuyerId,
        Guid? PaymentMethodId,
        decimal TotalAmount,
        string Currency,
        bool IsPaymentDone,
        DateTime CreatedAt,
        IReadOnlyList<OrderResponse> Orders);

    public sealed record OrderResponse(
        Guid PaymentOrderId,
        Guid SellerId,
        decimal Amount,
        string Currency,
        string Status,
        DateTime CreatedAt);

    internal sealed class Handler(PaymentQueryDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            var checkout = await context.PaymentEvents
                .Where(e => e.BookingId == request.BookingId && e.BuyerId == request.CallerId)
                .Select(e => new Response(
                    e.CheckoutId,
                    e.BookingId,
                    e.BuyerId,
                    e.PaymentMethodId,
                    e.TotalAmount,
                    e.Currency,
                    e.IsPaymentDone,
                    e.CreatedAt,
                    e.PaymentOrders
                        .Select(o => new OrderResponse(o.PaymentOrderId, o.SellerId, o.Amount, o.Currency, o.Status, o.CreatedAt))
                        .ToList()))
                .SingleOrDefaultAsync(cancellationToken);

            if (checkout is null)
                return Error.NotFound("checkout_not_found", $"No checkout was found for booking {request.BookingId}.");

            return checkout;
        }
    }
}

public sealed class GetCheckoutEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/payments/checkouts/{bookingId:long}",
            async (long bookingId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new GetCheckout.Query(callerId, bookingId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
