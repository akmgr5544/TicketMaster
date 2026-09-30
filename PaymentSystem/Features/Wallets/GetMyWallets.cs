using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.Wallets;

public static class GetMyWallets
{
    public sealed record Query(Guid OwnerId) : IRequest<Result<Response>>;

    public sealed record Response(IReadOnlyList<WalletBalance> Wallets);

    public sealed record WalletBalance(
        Guid WalletId,
        string Currency,
        decimal Balance,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Not paginated: the unique (OwnerId, Currency) index caps this at one row per currency.
            var wallets = await context.Wallets
                .Where(w => w.OwnerId == request.OwnerId)
                .OrderBy(w => w.Currency)
                .Select(w => new WalletBalance(w.WalletId, w.Currency, w.Balance, w.CreatedAt, w.UpdatedAt))
                .ToListAsync(cancellationToken);

            return new Response(wallets);
        }
    }
}

public sealed class GetMyWalletsEndpoints : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/wallets/me",
            async (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var ownerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new GetMyWallets.Query(ownerId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
