using System.Net;
using Microsoft.Extensions.Options;
using PaymentProvider.Configuration;
using PaymentProvider.Contracts.Stripe;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using Stripe;

namespace PaymentProvider.Providers.Stripe;

// The only type that touches Stripe.net: SDK objects and exceptions are translated here so the
// adapter works purely with the internal contracts.
internal sealed class StripeApi(IOptions<StripeOptions> options) : IStripeApi
{
    private const string PaymentOrderIdKey = "payment_order_id";

    private readonly StripeClient _client = new(options.Value.SecretKey, apiBase: options.Value.ApiBase);
    private readonly string _webhookSecret = options.Value.WebhookSecret;

    public async Task<StripePaymentIntent> CreatePaymentIntentAsync(StripeCreatePaymentIntent request, CancellationToken cancellationToken)
    {
        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = request.Amount,
            Currency = request.Currency,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = new Dictionary<string, string> { [PaymentOrderIdKey] = request.PaymentOrderId },
        };
        var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };

        var intent = await CallAsync(
            () => _client.V1.PaymentIntents.CreateAsync(createOptions, requestOptions, cancellationToken),
            cancellationToken);
        return ToContract(intent);
    }

    public async Task<StripePaymentIntent?> GetPaymentIntentAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var intent = await CallAsync(
                () => _client.V1.PaymentIntents.GetAsync(id, cancellationToken: cancellationToken),
                cancellationToken);
            return ToContract(intent);
        }
        catch (PaymentProviderException exception)
            when (exception.InnerException is StripeException { HttpStatusCode: HttpStatusCode.NotFound })
        {
            return null;
        }
    }

    public async Task<StripePaymentIntent?> FindPaymentIntentAsync(string paymentOrderId, CancellationToken cancellationToken)
    {
        // Search is eventually consistent (Stripe indexes within about a minute), which is fine for
        // reconciliation but means a just-created intent may not be found yet.
        var searchOptions = new PaymentIntentSearchOptions { Query = $"metadata['{PaymentOrderIdKey}']:'{paymentOrderId}'" };

        var results = await CallAsync(
            () => _client.V1.PaymentIntents.SearchAsync(searchOptions, cancellationToken: cancellationToken),
            cancellationToken);
        var intent = results.Data.MaxBy(candidate => candidate.Created);
        return intent is null ? null : ToContract(intent);
    }

    public async Task<StripeRefund> CreateRefundAsync(StripeCreateRefund request, CancellationToken cancellationToken)
    {
        var createOptions = new RefundCreateOptions
        {
            PaymentIntent = request.PaymentIntentId,
            Amount = request.Amount,
            Metadata = new Dictionary<string, string> { [PaymentOrderIdKey] = request.PaymentOrderId },
        };
        var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };

        try
        {
            var refund = await CallAsync(
                () => _client.V1.Refunds.CreateAsync(createOptions, requestOptions, cancellationToken),
                cancellationToken);
            return ToContract(refund);
        }
        // The idempotency key only lasts 24 hours. Past that, a second request for the same intent is refused as
        // already refunded, and the refund that did that is the answer.
        catch (PaymentProviderException exception)
            when (exception.InnerException is StripeException { StripeError.Code: "charge_already_refunded" })
        {
            var refunds = await CallAsync(
                () => _client.V1.Refunds.ListAsync(
                    new RefundListOptions { PaymentIntent = request.PaymentIntentId }, cancellationToken: cancellationToken),
                cancellationToken);
            var latest = refunds.Data.MaxBy(refund => refund.Created)
                         ?? throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Unknown,
                             $"Payment intent {request.PaymentIntentId} is refunded but lists no refund.", exception);
            return ToContract(latest);
        }
    }

    public StripeEvent ParseEvent(string body, string signatureHeader)
    {
        // Verified with the version check off first, so a forgery and a version mismatch are told
        // apart: only a body that is genuinely from Stripe gets as far as the version check.
        var stripeEvent = Construct(body, signatureHeader, checkApiVersion: false);
        if (stripeEvent is not { Id: not null, Type: not null, Data: not null })
        {
            throw new PaymentProviderException(
                PaymentProviderKind.Stripe, PaymentProviderErrorKind.Unknown, "The signed webhook body is not a Stripe event.");
        }

        Construct(body, signatureHeader, checkApiVersion: true);

        var intent = stripeEvent.Data.Object as PaymentIntent;
        return new StripeEvent(stripeEvent.Id, stripeEvent.Type, intent is null ? null : ToContract(intent));
    }

    private Event? Construct(string body, string signatureHeader, bool checkApiVersion)
    {
        try
        {
            return EventUtility.ConstructEvent(body, signatureHeader, _webhookSecret, throwOnApiVersionMismatch: checkApiVersion);
        }
        catch (StripeException exception)
        {
            // Stripe's endpoint and this SDK disagree on the API version, so payloads may not
            // deserialise faithfully. That is ours to fix, not an attack.
            var kind = checkApiVersion ? PaymentProviderErrorKind.Configuration : PaymentProviderErrorKind.InvalidSignature;
            throw new PaymentProviderException(PaymentProviderKind.Stripe, kind, exception.Message, exception);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or Newtonsoft.Json.JsonException)
        {
            throw new PaymentProviderException(
                PaymentProviderKind.Stripe, PaymentProviderErrorKind.Unknown, "The signed webhook body is not valid JSON.", exception);
        }
    }

    private static StripePaymentIntent ToContract(PaymentIntent intent) =>
        new(
            intent.Id,
            ToStatus(intent.Status),
            intent.ClientSecret,
            intent.Metadata.GetValueOrDefault(PaymentOrderIdKey),
            intent.LastPaymentError?.Message);

    private static StripeRefund ToContract(Refund refund) =>
        new(refund.Id, ToRefundStatus(refund.Status), refund.FailureReason);

    private static StripeRefundStatus ToRefundStatus(string status) =>
        status switch
        {
            "pending" => StripeRefundStatus.Pending,
            "requires_action" => StripeRefundStatus.RequiresAction,
            "succeeded" => StripeRefundStatus.Succeeded,
            "failed" => StripeRefundStatus.Failed,
            "canceled" => StripeRefundStatus.Canceled,
            _ => StripeRefundStatus.Unknown,
        };

    private static StripePaymentIntentStatus ToStatus(string status) =>
        status switch
        {
            "requires_payment_method" => StripePaymentIntentStatus.RequiresPaymentMethod,
            "requires_confirmation" => StripePaymentIntentStatus.RequiresConfirmation,
            "requires_action" => StripePaymentIntentStatus.RequiresAction,
            "processing" => StripePaymentIntentStatus.Processing,
            "requires_capture" => StripePaymentIntentStatus.RequiresCapture,
            "succeeded" => StripePaymentIntentStatus.Succeeded,
            "canceled" => StripePaymentIntentStatus.Canceled,
            _ => StripePaymentIntentStatus.Unknown,
        };

    private static async Task<T> CallAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (StripeException exception)
        {
            throw new PaymentProviderException(PaymentProviderKind.Stripe, ToErrorKind(exception), exception.Message, exception);
        }
        catch (HttpRequestException exception)
        {
            throw Unreachable(exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unreachable(exception);
        }
    }

    private static PaymentProviderErrorKind ToErrorKind(StripeException exception) =>
        exception switch
        {
            // 409 is a concurrent request on the same idempotency key; Stripe says to retry it.
            { HttpStatusCode: HttpStatusCode.Conflict } => PaymentProviderErrorKind.Transient,
            { HttpStatusCode: HttpStatusCode.TooManyRequests } => PaymentProviderErrorKind.Transient,
            { HttpStatusCode: >= HttpStatusCode.InternalServerError } => PaymentProviderErrorKind.Transient,
            { StripeError.Type: "api_connection_error" } => PaymentProviderErrorKind.Transient,
            { HttpStatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => PaymentProviderErrorKind.Configuration,
            _ => PaymentProviderErrorKind.InvalidRequest,
        };

    // A connection failure or timeout is indeterminate: the request may have reached Stripe.
    // Retrying with the same idempotency key is safe.
    private static PaymentProviderException Unreachable(Exception exception) =>
        new(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Transient, "Stripe could not be reached.", exception);
}
