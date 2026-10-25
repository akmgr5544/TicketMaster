using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentMethods;

public static class DeletePaymentMethod
{
    public sealed record Command(Guid CallerId, Guid PaymentMethodId) : IRequest<Result>, ITransactionalRequest;

    internal sealed class Handler(PaymentDbContext context) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var userId = request.CallerId;

            var method = await context.PaymentMethods
                .SingleOrDefaultAsync(m => m.Id == request.PaymentMethodId && m.UserId == userId, cancellationToken);

            if (method is null)
                return Error.NotFound("payment_method_not_found", $"Payment method {request.PaymentMethodId} was not found.");

            // A checkout keeps the method it was paid with; the foreign key would refuse the delete anyway.
            var usedByCheckout = await context.PaymentEvents
                .AnyAsync(e => e.PaymentMethodId == method.Id, cancellationToken);

            if (usedByCheckout)
                return Error.Conflict("payment_method_in_use", "This payment method was used for a checkout and cannot be removed.");

            context.PaymentMethods.Remove(method);
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public sealed class DeletePaymentMethodEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("api/payments/payment-methods/{paymentMethodId:guid}",
            async (Guid paymentMethodId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new DeletePaymentMethod.Command(callerId, paymentMethodId), cancellationToken);
                return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
            });
    }
}
