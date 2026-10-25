using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentMethods;

public static class GetPaymentMethod
{
    public sealed record Query(Guid CallerId, Guid PaymentMethodId) : IRequest<Result<Response>>;

    public sealed record Response(Guid Id, string PspName, bool IsDefault);

    internal sealed class Handler(PaymentQueryDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            var userId = request.CallerId.ToString();

            var method = await context.PaymentMethods
                .Where(m => m.Id == request.PaymentMethodId && m.UserId == userId)
                .Select(m => new Response(m.Id, m.PspName, m.IsDefault))
                .SingleOrDefaultAsync(cancellationToken);

            if (method is null)
                return Error.NotFound("payment_method_not_found", $"Payment method {request.PaymentMethodId} was not found.");

            return method;
        }
    }
}

public sealed class GetPaymentMethodEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/payments/payment-methods/{paymentMethodId:guid}",
            async (Guid paymentMethodId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new GetPaymentMethod.Query(callerId, paymentMethodId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
