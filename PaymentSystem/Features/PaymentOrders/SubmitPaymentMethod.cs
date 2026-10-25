using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Messaging;
using PaymentSystem.Shared.Results;
using Wolverine;

namespace PaymentSystem.Features.PaymentOrders;

// Hands a payment method to the PSP: the payment form's one-time token, or — when none is sent — the saved method the
// buyer chose when starting the checkout. Braintree charges here and answers at once; Stripe answers by webhook.
public static class SubmitPaymentMethod
{
    public sealed record Request(string? PaymentMethod);

    // Not ITransactionalRequest: the PSP is charged with no transaction open. The outcome is then recorded by
    // RecordPaymentOutcome, the same handler a webhook uses, in its own transaction.
    public sealed record Command(Guid CallerId, Guid PaymentOrderId, string? PaymentMethod) : IRequest<Result<Response>>;

    public sealed record Response(string Status, string? FailureReason);

    internal sealed class Handler(PaymentQueryDbContext context, IPaymentGatewayFactory gateways, IMessageBus bus)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var order = await context.PaymentOrders
                .SingleOrDefaultAsync(o => o.PaymentOrderId == request.PaymentOrderId
                                           && context.PaymentEvents.Any(e => e.CheckoutId == o.CheckoutId && e.BuyerId == request.CallerId),
                    cancellationToken);

            if (order is null)
                return Error.NotFound("payment_order_not_found", $"Payment order {request.PaymentOrderId} was not found.");

            if (order.Status != nameof(PaymentOrderStatus.Executing))
                return Error.Conflict("payment_order_not_executing", $"Payment order {request.PaymentOrderId} is {order.Status}; start its checkout first.");

            var paymentMethod = request.PaymentMethod;
            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                paymentMethod = await context.PaymentEvents
                    .Where(e => e.CheckoutId == order.CheckoutId)
                    .Join(context.PaymentMethods, e => e.PaymentMethodId, m => m.Id, (_, m) => m.PspToken)
                    .SingleOrDefaultAsync(cancellationToken);

                if (paymentMethod is null)
                    return Error.BadRequest("payment_method_required",
                        "Send the payment form's token, or choose a saved payment method when starting the checkout.");
            }

            PaymentResult? result;
            try
            {
                result = await gateways.ForProvider(order.Provider).SubmitPaymentMethodAsync(
                    new SubmitPaymentMethodRequest(order.PaymentOrderId, order.Amount, order.Currency, paymentMethod), cancellationToken);
            }
            catch (PaymentProviderException exception) when (Error.FromProvider(exception) is { } error)
            {
                return error;
            }

            // Stripe: the client pays on Stripe's page and the answer comes by webhook.
            if (result is null)
                return new Response("Pending", null);

            // Only Succeeded and Canceled are final; after Failed the buyer may try another payment method.
            if (result.Status is PaymentStatus.Succeeded or PaymentStatus.Canceled)
                await bus.InvokeAsync(
                    new RecordPaymentOutcome(order.PaymentOrderId, result.Status == PaymentStatus.Succeeded, result.ProviderReference),
                    cancellationToken);

            return new Response(result.Status.ToString(), result.FailureReason);
        }
    }
}

public sealed class SubmitPaymentMethodEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/orders/{paymentOrderId:guid}/payment-method",
            async (Guid paymentOrderId, SubmitPaymentMethod.Request body, HttpContext httpContext, ISender sender,
                CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(
                    new SubmitPaymentMethod.Command(callerId, paymentOrderId, body.PaymentMethod), cancellationToken);

                if (!result.IsSuccess)
                    return result.Error!.ToProblem();

                // Accepted, not OK: with Stripe nothing is decided until the webhook arrives.
                return result.Value!.Status == "Pending" ? Results.Accepted(value: result.Value) : Results.Ok(result.Value);
            });
    }
}
