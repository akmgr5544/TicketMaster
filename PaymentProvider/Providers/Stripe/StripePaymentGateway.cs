using PaymentProvider.Abstractions;
using PaymentProvider.Contracts.Stripe;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentProvider.Providers.Stripe;

internal sealed class StripePaymentGateway(IStripeApi api) : IPaymentGateway
{
    private const string SignatureHeader = "Stripe-Signature";
    private const string PaymentIntentEventPrefix = "payment_intent.";

    public PaymentProviderKind Kind => PaymentProviderKind.Stripe;

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.PaymentOrderId(Kind, request.PaymentOrderId);
        var currency = RequestGuard.Currency(Kind, request.Currency);

        var paymentOrderId = request.PaymentOrderId.ToString();
        var createRequest = new StripeCreatePaymentIntent(
            StripeAmount.ToMinorUnits(request.Amount, currency),
            currency.ToLowerInvariant(),
            paymentOrderId,
            // Keyed on the order so a redelivered request replays the first intent instead of creating a second.
            IdempotencyKey: $"checkout:{paymentOrderId}");

        var intent = await api.CreatePaymentIntentAsync(createRequest, cancellationToken);
        return new CheckoutSession(intent.Id, intent.ClientSecret);
    }

    public Task<PaymentResult?> SubmitPaymentMethodAsync(SubmitPaymentMethodRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<PaymentResult?>(null);

    public async Task<PaymentResult?> LookupAsync(PaymentLookupRequest request, CancellationToken cancellationToken = default)
    {
        var intent = request.ProviderReference is { } reference
            ? await api.GetPaymentIntentAsync(reference, cancellationToken)
            : await api.FindPaymentIntentAsync(request.PaymentOrderId.ToString(), cancellationToken);

        return intent is null ? null : ToResult(intent);
    }

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.PaymentOrderId(Kind, request.PaymentOrderId);
        var currency = RequestGuard.Currency(Kind, request.Currency);

        var intent = request.ProviderReference is { } reference
            ? await api.GetPaymentIntentAsync(reference, cancellationToken)
            : await api.FindPaymentIntentAsync(request.PaymentOrderId.ToString(), cancellationToken);
        if (intent is not { Status: StripePaymentIntentStatus.Succeeded })
            throw RequestGuard.Invalid(Kind, $"Payment order {request.PaymentOrderId} has no successful payment to refund.");

        var paymentOrderId = request.PaymentOrderId.ToString();
        var refundId = request.RefundId.ToString();
        var refund = await api.CreateRefundAsync(
            new StripeCreateRefund(
                intent.Id,
                StripeAmount.ToMinorUnits(request.Amount, currency),
                paymentOrderId,
                refundId,
                // Keyed on the refund as well as the order: an order refunded in parts takes one refund per part, a
                // whole-checkout refund shares its id across orders, and a redelivered part replays its first answer.
                IdempotencyKey: $"refund:{paymentOrderId}:{refundId}"),
            cancellationToken);

        return new RefundResult(refund.Id, ToStatus(refund), refund.FailureReason);
    }

    public WebhookEvent? ParseWebhook(WebhookRequest request)
    {
        var signature = request.GetHeader(SignatureHeader)
            ?? throw new PaymentProviderException(
                Kind, PaymentProviderErrorKind.InvalidSignature, $"The {SignatureHeader} header is missing.");

        var stripeEvent = api.ParseEvent(request.Body, signature);
        if (!stripeEvent.Type.StartsWith(PaymentIntentEventPrefix, StringComparison.Ordinal)
            || stripeEvent.PaymentIntent is not { } intent)
        {
            return null;
        }

        var paymentOrderId = Guid.TryParse(intent.PaymentOrderId, out var parsed) ? parsed : (Guid?)null;
        return new WebhookEvent(stripeEvent.Id, paymentOrderId, ToResult(intent));
    }

    private PaymentResult ToResult(StripePaymentIntent intent) =>
        new(intent.Id, ToStatus(intent), intent.LastPaymentError);

    private RefundStatus ToStatus(StripeRefund refund) =>
        refund.Status switch
        {
            StripeRefundStatus.Pending or StripeRefundStatus.RequiresAction => RefundStatus.Pending,
            StripeRefundStatus.Succeeded => RefundStatus.Succeeded,
            StripeRefundStatus.Failed or StripeRefundStatus.Canceled => RefundStatus.Failed,
            _ => throw new PaymentProviderException(
                Kind, PaymentProviderErrorKind.Unknown, $"Refund {refund.Id} has an unrecognised status."),
        };

    private PaymentStatus ToStatus(StripePaymentIntent intent) =>
        intent.Status switch
        {
            // A declined attempt returns the intent to requires_payment_method with the error attached.
            StripePaymentIntentStatus.RequiresPaymentMethod when intent.LastPaymentError is not null => PaymentStatus.Failed,
            StripePaymentIntentStatus.RequiresPaymentMethod => PaymentStatus.AwaitingPaymentMethod,
            StripePaymentIntentStatus.RequiresConfirmation => PaymentStatus.AwaitingPaymentMethod,
            StripePaymentIntentStatus.RequiresAction => PaymentStatus.RequiresAction,
            StripePaymentIntentStatus.Processing => PaymentStatus.Processing,
            StripePaymentIntentStatus.RequiresCapture => PaymentStatus.Authorized,
            StripePaymentIntentStatus.Succeeded => PaymentStatus.Succeeded,
            StripePaymentIntentStatus.Canceled => PaymentStatus.Canceled,
            _ => throw new PaymentProviderException(
                Kind, PaymentProviderErrorKind.Unknown, $"Payment intent {intent.Id} has an unrecognised status."),
        };
}
