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
        bool IsPaymentDone,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<Order> Orders);

    public sealed record Order(
        Guid PaymentOrderId,
        Guid MerchantId,
        decimal Amount,
        string Currency,
        string Status,
        bool WalletUpdated,
        bool LedgerUpdated,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            // The buyer is in the predicate, so another buyer's checkout is indistinguishable from none.
            // Orders are not paginated: a checkout holds one per seller of a single booking.
            var checkout = await context.PaymentEvents
                .Where(e => e.BookingId == request.BookingId && e.BuyerId == request.CallerId)
                .Select(e => new Response(
                    e.CheckoutId,
                    e.BookingId,
                    e.IsPaymentDone,
                    e.CreatedAt,
                    e.UpdatedAt,
                    e.PaymentOrders
                        .OrderBy(o => o.PaymentOrderId)
                        .Select(o => new Order(
                            o.PaymentOrderId,
                            o.MerchantId,
                            o.Amount,
                            o.Currency,
                            o.Status.ToString(),
                            o.WalletUpdated,
                            o.LedgerUpdated,
                            o.CreatedAt,
                            o.UpdatedAt))
                        .ToList()))
                .SingleOrDefaultAsync(cancellationToken);

            if (checkout is null)
                return Error.NotFound("checkout_not_found", $"No checkout for booking {request.BookingId}.");

            return checkout;
        }
    }
}

public sealed class GetCheckoutEndpoints : IEndpointMarker
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
