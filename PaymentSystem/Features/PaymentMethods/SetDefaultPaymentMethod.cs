using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Data.Configurations.Domain;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentMethods;

// The only change a saved method takes: a different card is a different PSP token, so it is added, not edited.
public static class SetDefaultPaymentMethod
{
    public sealed record Command(Guid CallerId, Guid PaymentMethodId) : IRequest<Result>, ITransactionalRequest;

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var userId = request.CallerId;

            var methods = await context.PaymentMethods
                .Where(m => m.UserId == userId)
                .ToListAsync(cancellationToken);

            var target = methods.SingleOrDefault(m => m.Id == request.PaymentMethodId);
            if (target is null)
                return Error.NotFound("payment_method_not_found", $"Payment method {request.PaymentMethodId} was not found.");

            if (target.IsDefault)
                return Result.Success();

            try
            {
                // Saved before the new default is set: one save doing both could set the new default before
                // clearing the old one and trip the one-default index.
                foreach (var other in methods)
                {
                    other.ClearDefault();
                }
                
                await context.SaveChangesAsync(cancellationToken);

                target.MarkAsDefault();
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                       {
                           SqlState: PostgresErrorCodes.UniqueViolation,
                           ConstraintName: PaymentMethodConfiguration.DefaultPerUserIndex
                       })
            {
                return Error.Conflict("default_payment_method_changed",
                    "Your default payment method was changed at the same time. Try again.");
            }

            return Result.Success();
        }
    }
}

public sealed class SetDefaultPaymentMethodEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("api/payments/payment-methods/{paymentMethodId:guid}/default",
            async (Guid paymentMethodId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new SetDefaultPaymentMethod.Command(callerId, paymentMethodId), cancellationToken);
                return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
            });
    }
}
