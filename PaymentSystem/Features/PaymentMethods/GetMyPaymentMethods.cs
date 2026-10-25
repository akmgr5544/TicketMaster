using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;

namespace PaymentSystem.Features.PaymentMethods;

public static class GetMyPaymentMethods
{
    public sealed record Query(Guid CallerId) : IRequest<IReadOnlyList<Response>>;

    public sealed record Response(Guid Id, string PspName, bool IsDefault);

    internal sealed class Handler(PaymentQueryDbContext context) : IRequestHandler<Query, IReadOnlyList<Response>>
    {
        public async Task<IReadOnlyList<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            // PaymentMethod.UserId is a string column, so the caller's id is compared in its text form.
            var callerId = request.CallerId.ToString();

            return await context.PaymentMethods
                .Where(m => m.UserId == callerId)
                .OrderByDescending(m => m.IsDefault)
                .Select(m => new Response(m.Id, m.PspName, m.IsDefault))
                .ToListAsync(cancellationToken);
        }
    }
}

public sealed class GetMyPaymentMethodsEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/payments/payment-methods/me",
            async (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                return Results.Ok(await sender.Send(new GetMyPaymentMethods.Query(callerId), cancellationToken));
            });
    }
}
