using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentOrders;

// Opens the PSP's payment page for an order. The order id is the PSP's idempotency key, so asking twice reaches the
// same payment rather than a second one.
public static class StartCheckout
{
    // Not ITransactionalRequest: the PSP call must not hold a database transaction open, and the single save
    // afterwards is atomic on its own.
    public sealed record Command(Guid CallerId, Guid PaymentOrderId) : IRequest<Result<Response>>;

    // The client token goes to the payment form once; it is never stored or logged.
    public sealed record Response(string Provider, string ClientToken);

    internal sealed class Handler(PaymentDbContext context, IPaymentGatewayFactory gateways)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var checkout = await context.PaymentEvents
                .SingleOrDefaultAsync(e => e.BuyerId == request.CallerId
                                           && e.PaymentOrders.Any(o => o.PaymentOrderId == request.PaymentOrderId), cancellationToken);

            if (checkout is null)
                return Error.NotFound("payment_order_not_found", $"Payment order {request.PaymentOrderId} was not found.");

            var order = checkout.FindOrder(request.PaymentOrderId);
            if (order.Status is PaymentOrderStatus.Success or PaymentOrderStatus.Failed or PaymentOrderStatus.Refunded)
                return Error.Conflict("payment_order_settled", $"Payment order {request.PaymentOrderId} is already {order.Status}.");

            // An order already started goes back to its own provider.
            var gateway = order.Status == PaymentOrderStatus.Executing ? gateways.ForProvider(order.Provider) : gateways.Default;

            CheckoutSession session;
            try
            {
                session = await gateway.CreateCheckoutAsync(
                    new CheckoutRequest(order.PaymentOrderId, order.Amount.Amount, order.Amount.Currency), cancellationToken);
            }
            catch (PaymentProviderException exception) when (Error.FromProvider(exception) is { } error)
            {
                return error;
            }

            checkout.StartOrder(order.PaymentOrderId, gateway.Kind.ToString(), session.ProviderReference);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            // The expiry, a cancellation or a PSP answer changed the order while the PSP was being asked.
            catch (DbUpdateConcurrencyException)
            {
                return Error.Conflict("payment_order_changed", "The payment order changed while it was being started. Try again.");
            }

            return new Response(gateway.Kind.ToString(), session.ClientToken);
        }
    }
}

public sealed class StartCheckoutEndpoint : IEndpointMarker
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
