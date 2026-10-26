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
    // A saved method of the buyer's to pay with; without one the buyer enters a card on the PSP's page.
    public sealed record Request(Guid? PaymentMethodId);

    // Not ITransactionalRequest: the PSP call must not hold a database transaction open, and the single save
    // afterwards is atomic on its own.
    public sealed record Command(Guid CallerId, Guid PaymentOrderId, Guid? PaymentMethodId = null) : IRequest<Result<Response>>;

    // The client token goes to the payment form once; it is never stored or logged.
    public sealed record Response(string Provider, string ClientToken);

    internal sealed class Handler(PaymentDbContext context, IPaymentGatewayFactory gateways, TimeProvider clock)
        : IRequestHandler<Command, Result<Response>>
    {
        // A race lost to the expiry, a cancellation, a PSP answer or the buyer's own second request is decided again
        // over the winner's state rather than handed back as a 409. Asking the PSP again is safe: the order id is its
        // idempotency key, so the same payment comes back.
        private const int MaxAttempts = 3;

        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await StartAsync(request, cancellationToken);
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
                {
                    context.ChangeTracker.Clear();
                }
                // Every attempt lost to a concurrent writer; rare enough that the caller can try again.
                catch (DbUpdateConcurrencyException)
                {
                    return Error.Conflict("payment_order_changed", "The payment order kept changing while it was being started. Try again.");
                }
            }
        }

        private async Task<Result<Response>> StartAsync(Command request, CancellationToken cancellationToken)
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

            if (request.PaymentMethodId is { } paymentMethodId)
            {
                var method = await context.PaymentMethods
                    .SingleOrDefaultAsync(m => m.Id == paymentMethodId && m.UserId == request.CallerId, cancellationToken);

                if (method is null)
                    return Error.NotFound("payment_method_not_found", $"Payment method {paymentMethodId} was not found.");

                // A saved token belongs to the PSP that issued it; another PSP cannot charge it.
                if (!string.Equals(method.PspName, gateway.Kind.ToString(), StringComparison.OrdinalIgnoreCase))
                    return Error.BadRequest("payment_method_provider_mismatch",
                        $"Payment method {paymentMethodId} is held by {method.PspName}, but this order is paid through {gateway.Kind}.");

                checkout.UsePaymentMethod(method.Id);
            }

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

            checkout.StartOrder(order.PaymentOrderId, gateway.Kind.ToString(), session.ProviderReference, clock.GetUtcNow().UtcDateTime);
            await context.SaveChangesAsync(cancellationToken);

            return new Response(gateway.Kind.ToString(), session.ClientToken);
        }
    }
}

public sealed class StartCheckoutEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/orders/{paymentOrderId:guid}/checkout",
            async (Guid paymentOrderId, StartCheckout.Request? body, HttpContext httpContext, ISender sender,
                CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(
                    new StartCheckout.Command(callerId, paymentOrderId, body?.PaymentMethodId), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
