using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Messaging;
using Wolverine;

namespace PaymentSystem.Features.Webhooks;

// A PSP calling back about a payment or a refund. The gateway does not authenticate this route — a PSP sends no user
// token — so the provider's signature check is its only protection. Repeats and out-of-order deliveries are safe: only
// a final status changes an order or a refund, and a repeated outcome is a no-op.
public sealed class HandleWebhookEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/payments/webhooks/{provider}",
            async (string provider, HttpRequest request, IPaymentGatewayFactory gateways, IMessageBus bus,
                PaymentQueryDbContext queries, CancellationToken cancellationToken) =>
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

                if (webhook?.Refund is { } refund)
                {
                    await RecordRefundAsync(webhook.PaymentOrderId, refund, queries, bus, cancellationToken);
                    return Results.Ok();
                }

                // Verified, but not about one of our orders, or not a final status: acknowledged so the PSP stops
                // resending it. Failed is not final — the buyer may retry with another payment method.
                if (webhook?.PaymentOrderId is not { } paymentOrderId
                    || webhook.Payment?.Status is not (PaymentStatus.Succeeded or PaymentStatus.Canceled))
                    return Results.Ok();

                await bus.InvokeAsync(
                    new RecordPaymentOutcome(paymentOrderId, webhook.Payment.Status == PaymentStatus.Succeeded, webhook.Payment.ProviderReference),
                    cancellationToken);

                return Results.Ok();
            });
    }

    // Only a final answer is recorded; a refund still pending at the PSP is the reconciler's to follow. A refund nobody
    // here asked for is acknowledged and ignored.
    private static async Task RecordRefundAsync(Guid? paymentOrderId, RefundUpdate update, PaymentQueryDbContext queries,
        IMessageBus bus, CancellationToken cancellationToken)
    {
        if (update.Result.Status is not (RefundStatus.Succeeded or RefundStatus.Failed))
            return;

        var reference = update.Result.ProviderReference;
        var refundOrderId = paymentOrderId is { } orderId && update.RefundId is { } requestId
            ? await queries.RefundOrders
                .Where(r => r.PaymentOrderId == orderId && r.RequestId == requestId)
                .Select(r => (Guid?)r.RefundOrderId)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        refundOrderId ??= await queries.RefundOrders
            .Where(r => r.PspRefundReference == reference)
            .Select(r => (Guid?)r.RefundOrderId)
            .FirstOrDefaultAsync(cancellationToken);

        if (refundOrderId is not { } id)
            return;

        await bus.InvokeAsync(
            new RecordRefundOutcome(id, update.Result.Status == RefundStatus.Succeeded, reference, update.Result.FailureReason),
            cancellationToken);
    }
}
