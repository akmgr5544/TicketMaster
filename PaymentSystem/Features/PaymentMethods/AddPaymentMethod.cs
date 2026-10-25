using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Data.Configurations.Domain;
using PaymentSystem.Domain;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentMethods;

public static class AddPaymentMethod
{
    public sealed record Request(string PspName, string PspToken, bool MakeDefault);

    public sealed record Command(Guid CallerId, string PspName, string PspToken, bool MakeDefault)
        : IRequest<Result<Response>>, ITransactionalRequest;

    public sealed record Response(Guid Id, string PspName, bool IsDefault);

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var userId = request.CallerId;
            var method = PaymentMethod.Create(userId, request.PspName, request.PspToken);

            var existing = await context.PaymentMethods
                .Where(m => m.UserId == userId)
                .ToListAsync(cancellationToken);

            try
            {
                // A user's first method is their default whether or not they asked.
                if (request.MakeDefault || existing.Count == 0)
                {
                    // Saved before the new default is added: one save doing both could insert the new default
                    // before clearing the old one and trip the one-default index.
                    foreach (var other in existing)
                        other.ClearDefault();
                    await context.SaveChangesAsync(cancellationToken);

                    method.MarkAsDefault();
                }

                context.PaymentMethods.Add(method);
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

            return new Response(method.Id, method.PspName, method.IsDefault);
        }
    }
}

public sealed class AddPaymentMethodEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/payment-methods",
            async (AddPaymentMethod.Request body, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(
                    new AddPaymentMethod.Command(callerId, body.PspName, body.PspToken, body.MakeDefault), cancellationToken);

                return result.IsSuccess
                    ? Results.Created($"api/payments/payment-methods/{result.Value!.Id}", result.Value)
                    : result.Error!.ToProblem();
            });
    }
}
