using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentOrders;

public static class StartCheckout
{
    public sealed record Command(Guid CallerId, Guid PaymentOrderId) : IRequest<Result<Response>>, ITransactionalRequest;

    // ClientToken is the one provider secret that leaves this service: the client's payment form needs it.
    // It is never stored and never logged.
    public sealed record Response(Guid PaymentOrderId, string Provider, string ClientToken);

    internal sealed class Handler(PaymentDbContext context, IPaymentGatewayFactory gateways, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            // The buyer is in the predicate, so another buyer's order is indistinguishable from a missing one.
            var checkout = await context.PaymentEvents
                .Where(e => e.BuyerId == request.CallerId
                            && e.PaymentOrders.Any(o => o.PaymentOrderId == request.PaymentOrderId))
                .SingleOrDefaultAsync(cancellationToken);
            if (checkout is null)
                return Error.NotFound("payment_order_not_found", $"No payment order {request.PaymentOrderId}.");

            var order = checkout.Order(request.PaymentOrderId);
            if (order.Status is PaymentOrderStatus.Success or PaymentOrderStatus.Failed or PaymentOrderStatus.Refunded)
                return Error.Conflict("payment_order_settled", $"Payment order {order.PaymentOrderId} is already {order.Status}.");

            var gateway = gateways.ForProvider(order.Provider);
            CheckoutSession session;
            try
            {
                // The order id is the provider's idempotency key, so asking again for an order already started
                // replays the session it already has rather than opening a second one.
                session = await gateway.CreateCheckoutAsync(
                    new CheckoutRequest(order.PaymentOrderId, order.Amount, order.Currency), cancellationToken);
            }
            catch (PaymentProviderException exception) when (Error.FromProvider(exception) is { } error)
            {
                return error;
            }

            // A redelivered or reloaded checkout. The client token is never stored, so the provider is the only
            // place to get it again; the replayed session must be the recorded one, or the provider opened a
            // second payment (an expired idempotency key) and both could be paid — that is refused, not adopted.
            if (order.Status == PaymentOrderStatus.Executing)
            {
                if (order.PspToken != session.ProviderReference)
                {
                    logger.LogWarning("The provider returned a different session for payment order {PaymentOrderId}.",
                        order.PaymentOrderId);
                    return Error.Conflict("psp_session_mismatch",
                        $"Payment order {order.PaymentOrderId} already has a different payment session.");
                }

                return new Response(order.PaymentOrderId, gateway.Kind.ToString(), session.ClientToken);
            }

            try
            {
                checkout.StartExecuting(order.PaymentOrderId, gateway.Kind.ToString(), session.ProviderReference);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (PaymentDomainException exception)
            {
                return Error.BadRequest("payment_order_not_startable", exception.Message);
            }
            // Another write to this checkout landed first — a concurrent start, or a sibling order settling. The
            // session is idempotent, so the client simply asks again.
            catch (DbUpdateConcurrencyException)
            {
                return Error.Conflict("checkout_changed", "The checkout changed while starting. Try again.");
            }

            logger.LogInformation("Started payment order {PaymentOrderId} with {Provider}.", order.PaymentOrderId,
                gateway.Kind);
            return new Response(order.PaymentOrderId, gateway.Kind.ToString(), session.ClientToken);
        }
    }
}

public sealed class StartCheckoutEndpoints : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/orders/{paymentOrderId:guid}/checkout",
            async (Guid paymentOrderId, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(new StartCheckout.Command(callerId, paymentOrderId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
