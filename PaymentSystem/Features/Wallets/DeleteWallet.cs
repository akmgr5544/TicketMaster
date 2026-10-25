using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.Wallets;

public static class DeleteWallet
{
    public sealed record Command(Guid CallerId, Guid WalletId) : IRequest<Result>, ITransactionalRequest;

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var wallet = await context.Wallets
                .SingleOrDefaultAsync(w => w.WalletId == request.WalletId && w.UserId == request.CallerId, cancellationToken);

            if (wallet is null)
                return Error.NotFound("wallet_not_found", $"Wallet {request.WalletId} was not found.");

            // Deleting a wallet that holds money would make that money disappear from the books.
            if (!wallet.CurrentBalance.IsZero)
                return Error.Conflict("wallet_not_empty", "Only an empty wallet can be removed.");

            context.Wallets.Remove(wallet);
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public sealed class DeleteWalletEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("api/wallets/{walletId:guid}",
            async (Guid walletId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new DeleteWallet.Command(callerId, walletId), cancellationToken);
                return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
            });
    }
}
