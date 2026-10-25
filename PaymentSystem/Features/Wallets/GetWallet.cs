using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.Wallets;

public static class GetWallet
{
    public sealed record Query(Guid CallerId, Guid WalletId) : IRequest<Result<Response>>;

    public sealed record Response(Guid WalletId, decimal CurrentBalance, string Currency, DateTime UpdatedAt);

    internal sealed class Handler(PaymentQueryDbContext context) : IRequestHandler<Query, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            var wallet = await context.Wallets
                .Where(w => w.WalletId == request.WalletId && w.UserId == request.CallerId)
                .Select(w => new Response(w.WalletId, w.CurrentBalance, w.Currency, w.UpdatedAt))
                .SingleOrDefaultAsync(cancellationToken);

            if (wallet is null)
                return Error.NotFound("wallet_not_found", $"Wallet {request.WalletId} was not found.");

            return wallet;
        }
    }
}

public sealed class GetWalletEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/wallets/{walletId:guid}",
            async (Guid walletId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new GetWallet.Query(callerId, walletId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
