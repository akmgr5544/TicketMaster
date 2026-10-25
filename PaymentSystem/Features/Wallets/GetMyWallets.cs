using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;

namespace PaymentSystem.Features.Wallets;

public static class GetMyWallets
{
    public sealed record Query(Guid CallerId) : IRequest<IReadOnlyList<Response>>;

    public sealed record Response(Guid WalletId, decimal CurrentBalance, string Currency, DateTime UpdatedAt);

    internal sealed class Handler(PaymentQueryDbContext context) : IRequestHandler<Query, IReadOnlyList<Response>>
    {
        public async Task<IReadOnlyList<Response>> Handle(Query request, CancellationToken cancellationToken) =>
            await context.Wallets
                .Where(w => w.UserId == request.CallerId)
                .OrderBy(w => w.Currency)
                .Select(w => new Response(w.WalletId, w.CurrentBalance, w.Currency, w.UpdatedAt))
                .ToListAsync(cancellationToken);
    }
}

public sealed class GetMyWalletsEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/wallets/me",
            async (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                return Results.Ok(await sender.Send(new GetMyWallets.Query(callerId), cancellationToken));
            });
    }
}
