using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Data.Configurations.Domain;
using PaymentSystem.Domain;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.Wallets;

// Opens an empty wallet. Its balance only ever moves through a settlement or a refund, which writes the ledger too.
public static class CreateWallet
{
    public sealed record Request(string Currency);

    public sealed record Command(Guid CallerId, string Currency) : IRequest<Result<Response>>, ITransactionalRequest;

    public sealed record Response(Guid WalletId, decimal CurrentBalance, string Currency);

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var wallet = new Wallet(request.CallerId, request.Currency);
            var currency = wallet.CurrentBalance.Currency;

            var exists = await context.Wallets
                .AnyAsync(w => w.UserId == request.CallerId && w.CurrentBalance.Currency == currency, cancellationToken);

            if (exists)
                return Error.Conflict("wallet_exists", $"You already have a {currency} wallet.");

            context.Wallets.Add(wallet);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            // Another request created the same wallet between the check above and this insert.
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                       {
                           SqlState: PostgresErrorCodes.UniqueViolation,
                           ConstraintName: WalletConfiguration.UserCurrencyIndex
                       })
            {
                return Error.Conflict("wallet_exists", $"You already have a {currency} wallet.");
            }

            return new Response(wallet.WalletId, wallet.CurrentBalance.Amount, currency);
        }
    }
}

public sealed class CreateWalletEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/wallets",
            async (CreateWallet.Request body, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new CreateWallet.Command(callerId, body.Currency), cancellationToken);
                return result.IsSuccess
                    ? Results.Created($"api/wallets/{result.Value!.WalletId}", result.Value)
                    : result.Error!.ToProblem();
            });
    }
}
