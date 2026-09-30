using PaymentSystem.Shared.Psp;
using MediatR;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.PaymentOrders;

public static class SubmitPaymentMethod
{
    public sealed record Body(string PaymentMethod);

    public sealed record Command(Guid CallerId, Guid PaymentOrderId, string PaymentMethod)
        : IRequest<Result<Response>>, ITransactionalRequest;

    // Pending: the provider takes the payment method from the client directly and reports by webhook
    // (Stripe). Otherwise the charge already ran; a Failed ProviderStatus leaves the order open for another
    // payment method.
    public sealed record Response(
        Guid PaymentOrderId,
        bool Pending,
        string? ProviderStatus,
        string? FailureReason,
        string OrderStatus);

    internal sealed class Handler(PaymentDbContext context, IPaymentGatewayFactory gateways, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.PaymentMethod))
                return Error.BadRequest("payment_method_missing", "A payment method is required.");

            var checkout = await ProviderOutcome.FindCheckoutAsync(context, request.PaymentOrderId, request.CallerId,
                cancellationToken);
            if (checkout is null)
                return Error.NotFound("payment_order_not_found", $"No payment order {request.PaymentOrderId}.");

            var order = ProviderOutcome.OrderIn(checkout, request.PaymentOrderId);
            switch (order.Status)
            {
                case PaymentOrderStatus.NotStarted:
                    return Error.Conflict("checkout_not_started", $"Payment order {order.PaymentOrderId} has no checkout yet.");
                case PaymentOrderStatus.Success or PaymentOrderStatus.Failed:
                    return Error.Conflict("payment_order_settled", $"Payment order {order.PaymentOrderId} is already {order.Status}.");
            }

            PaymentResult? payment;
            try
            {
                payment = await gateways.Default.SubmitPaymentMethodAsync(
                    new SubmitPaymentMethodRequest(order.PaymentOrderId, order.Amount, order.Currency, request.PaymentMethod),
                    cancellationToken);
            }
            catch (PaymentProviderException exception) when (ProviderOutcome.IsCallerFacing(exception))
            {
                return ProviderOutcome.ToError(exception);
            }

            if (payment is null)
                return new Response(order.PaymentOrderId, true, null, null, order.Status.ToString());

            var applied = await ProviderOutcome.ApplyAsync(context, logger, order.PaymentOrderId, payment.Status,
                cancellationToken);

            // The charge has already happened in both cases, so the buyer must not simply pay again; the order
            // needs reconciling against the provider.
            if (applied.Kind is ProviderOutcome.Kind.Conflict or ProviderOutcome.Kind.Superseded)
                return Error.Conflict("payment_outcome_not_recorded",
                    $"The payment for order {order.PaymentOrderId} was processed but could not be recorded.");

            return new Response(order.PaymentOrderId, false, payment.Status.ToString(), payment.FailureReason,
                (applied.OrderStatus ?? order.Status).ToString());
        }
    }
}

public sealed class SubmitPaymentMethodEndpoints : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/orders/{paymentOrderId:guid}/payment-method",
            async (Guid paymentOrderId, SubmitPaymentMethod.Body body, HttpContext httpContext, ISender sender,
                CancellationToken cancellationToken) =>
            {
                if (!httpContext.TryGetUserId(out var callerId))
                    return Results.Unauthorized();

                var result = await sender.Send(
                    new SubmitPaymentMethod.Command(callerId, paymentOrderId, body.PaymentMethod), cancellationToken);
                if (!result.IsSuccess)
                    return result.Error!.ToProblem();

                return result.Value!.Pending ? Results.Accepted(value: result.Value) : Results.Ok(result.Value);
            });
    }
}
