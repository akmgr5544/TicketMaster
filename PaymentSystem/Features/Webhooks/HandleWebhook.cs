using PaymentSystem.Shared.Psp;
using MediatR;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Features.Webhooks;

// Ungated at the gateway and carries no caller identity: the provider's signature is the only credential.
// Webhooks arrive at least once and out of order. EventId is not stored because nothing needs it to be: the
// only statuses that change an order are final, the domain treats repeating one as a no-op and refuses the
// opposite one, so a duplicate or a stale event lands where the order already is.
public static class HandleWebhook
{
    public sealed record Command(string Provider, string Body, IReadOnlyDictionary<string, string> Headers)
        : IRequest<Result<Response>>, ITransactionalRequest;

    // Anything answered 2xx is never redelivered, so every outcome that retrying cannot change is a success.
    public sealed record Response(string Outcome)
    {
        public static readonly Response Applied = new("applied");
        public static readonly Response Unchanged = new("unchanged");
        public static readonly Response Ignored = new("ignored");
    }

    internal sealed class Handler(PaymentDbContext context, IPaymentGatewayFactory gateways, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            // Matched by name only: Enum.TryParse would also accept "1" as a provider.
            var kind = Enum.GetValues<PaymentProviderKind>()
                .Cast<PaymentProviderKind?>()
                .FirstOrDefault(k => string.Equals(k.ToString(), request.Provider, StringComparison.OrdinalIgnoreCase));
            if (kind is null)
                return UnknownProvider(request.Provider);

            WebhookEvent? webhook;
            try
            {
                webhook = gateways.Get(kind.Value).ParseWebhook(new WebhookRequest(request.Body, request.Headers));
            }
            catch (PaymentProviderException exception) when (exception.Kind == PaymentProviderErrorKind.Configuration)
            {
                return UnknownProvider(request.Provider);
            }
            // 400 rather than 401: there is no credential to challenge for, the message itself is not authentic.
            catch (PaymentProviderException exception) when (exception.Kind is PaymentProviderErrorKind.InvalidSignature
                                                                  or PaymentProviderErrorKind.InvalidRequest)
            {
                logger.LogWarning("Rejected a {Provider} webhook: {Reason}", kind, exception.Kind);
                return Error.BadRequest("invalid_webhook", "The webhook could not be verified.");
            }

            if (webhook is null)
                return Response.Ignored;

            if (webhook.PaymentOrderId is not { } paymentOrderId)
            {
                logger.LogWarning("{Provider} webhook {EventId} names no payment order.", kind, webhook.EventId);
                return Response.Ignored;
            }

            var applied = await ProviderOutcome.ApplyAsync(context, logger, paymentOrderId, webhook.Payment.Status,
                cancellationToken);

            switch (applied.Kind)
            {
                case ProviderOutcome.Kind.Applied:
                    logger.LogInformation("{Provider} webhook {EventId} settled payment order {PaymentOrderId} as {Status}.",
                        kind, webhook.EventId, paymentOrderId, applied.OrderStatus);
                    return Response.Applied;
                case ProviderOutcome.Kind.OrderNotFound:
                    // Not ours — another environment sharing the account. A retry would never find it either.
                    logger.LogWarning("{Provider} webhook {EventId} names unknown payment order {PaymentOrderId}.",
                        kind, webhook.EventId, paymentOrderId);
                    return Response.Ignored;
                // The provider retries a non-2xx, and by then the start or the other writer has committed.
                case ProviderOutcome.Kind.NotStarted:
                    return Error.Conflict("payment_order_not_started", $"Payment order {paymentOrderId} has not started.");
                case ProviderOutcome.Kind.Conflict:
                    return Error.Conflict("checkout_changed", "The checkout changed concurrently. Retry.");
                default:
                    return Response.Unchanged;
            }
        }

        private static Error UnknownProvider(string provider) =>
            Error.NotFound("unknown_provider", $"No payment provider '{provider}' is configured.");
    }
}

public sealed class HandleWebhookEndpoints : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/webhooks/{provider}",
            async (string provider, HttpRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                // The raw body, never a bound model: the signature is computed over the exact bytes received.
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync(cancellationToken);
                var headers = request.Headers.ToDictionary(
                    header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

                var result = await sender.Send(new HandleWebhook.Command(provider, body, headers), cancellationToken);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
            });
    }
}
