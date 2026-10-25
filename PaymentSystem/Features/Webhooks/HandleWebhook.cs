using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Messaging;
using Wolverine;

namespace PaymentSystem.Features.Webhooks;

// A PSP calling back about a payment. The gateway does not authenticate this route — a PSP sends no user token — so
// the provider's signature check is its only protection. Repeats and out-of-order deliveries are safe: only a final
// status changes an order, and the domain treats a repeated outcome as a no-op.
public sealed class HandleWebhookEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/webhooks/{provider}",
            async (string provider, HttpRequest request, IPaymentGatewayFactory gateways, IMessageBus bus,
                CancellationToken cancellationToken) =>
            {
                if (!Enum.TryParse<PaymentProviderKind>(provider, ignoreCase: true, out var kind))
                    return Results.NotFound();

                // The signature is computed over the exact bytes received, so the body is read raw.
                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync(cancellationToken);
                var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString());

                WebhookEvent? webhook;
                try
                {
                    webhook = gateways.Get(kind).ParseWebhook(new WebhookRequest(body, headers));
                }
                catch (PaymentProviderException exception) when (exception.Kind == PaymentProviderErrorKind.InvalidSignature)
                {
                    return Results.BadRequest();
                }
                catch (PaymentProviderException exception) when (exception.Kind == PaymentProviderErrorKind.Configuration)
                {
                    return Results.NotFound();
                }

                // Verified, but not about one of our orders, or not a final status: acknowledged so the PSP stops
                // resending it. Failed is not final — the buyer may retry with another payment method.
                if (webhook?.PaymentOrderId is not { } paymentOrderId
                    || webhook.Payment.Status is not (PaymentStatus.Succeeded or PaymentStatus.Canceled))
                    return Results.Ok();

                await bus.InvokeAsync(
                    new RecordPaymentOutcome(paymentOrderId, webhook.Payment.Status == PaymentStatus.Succeeded, webhook.Payment.ProviderReference),
                    cancellationToken);

                return Results.Ok();
            });
    }
}
