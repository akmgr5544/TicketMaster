using System.Globalization;
using System.Web;
using Microsoft.Extensions.Options;
using PaymentProvider.Abstractions;
using PaymentProvider.Configuration;
using PaymentProvider.Contracts.Braintree;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentProvider.Providers.Braintree;

internal sealed class BraintreePaymentGateway(IBraintreeApi api, IOptions<BraintreeOptions> options) : IPaymentGateway
{
    private readonly Dictionary<string, string> _merchantAccounts =
        new(options.Value.MerchantAccounts, StringComparer.OrdinalIgnoreCase);

    public PaymentProviderKind Kind => PaymentProviderKind.Braintree;

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.PaymentOrderId(Kind, request.PaymentOrderId);

        // Braintree creates nothing before the sale, so there is no reference to return yet.
        var clientToken = await api.GenerateClientTokenAsync(MerchantAccountFor(request.Currency));
        return new CheckoutSession(ProviderReference: null, clientToken);
    }

    public async Task<PaymentResult?> SubmitPaymentMethodAsync(SubmitPaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.PaymentOrderId(Kind, request.PaymentOrderId);
        RequestGuard.PaymentMethod(Kind, request.PaymentMethod);
        RequestGuard.Amount(Kind, request.Amount);

        // Braintree sends amounts as "x.xx" whatever the currency; a third decimal would be rounded
        // by someone other than us.
        if (decimal.Round(request.Amount, 2) != request.Amount)
        {
            throw RequestGuard.Invalid(Kind, $"Braintree amounts have at most two decimal places, was {request.Amount.ToString(CultureInfo.InvariantCulture)}.");
        }

        var sale = new BraintreeSale(
            request.Amount,
            request.PaymentMethod,
            // Braintree has no idempotency key; the order id is what a timed-out sale is searched by.
            request.PaymentOrderId.ToString(),
            MerchantAccountFor(request.Currency),
            SubmitForSettlement: true);

        // Deliberately not cancellable: abandoning a sale in flight could leave a charge we never record.
        var transaction = await api.SaleAsync(sale);
        return ToResult(transaction);
    }

    public async Task<PaymentResult?> LookupAsync(PaymentLookupRequest request, CancellationToken cancellationToken = default)
    {
        var transaction = request.ProviderReference is { } reference
            ? await api.FindAsync(reference)
            : await api.FindLatestByOrderIdAsync(request.PaymentOrderId.ToString());

        return transaction is null ? null : ToResult(transaction);
    }

    public WebhookEvent? ParseWebhook(WebhookRequest request)
    {
        var form = HttpUtility.ParseQueryString(request.Body);
        var signature = form["bt_signature"];
        var payload = form["bt_payload"];
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(payload))
        {
            throw new PaymentProviderException(
                Kind, PaymentProviderErrorKind.InvalidSignature, "The bt_signature or bt_payload field is missing.");
        }

        // Card settlements are never pushed; only ACH/SEPA settlement notifications carry a transaction.
        var webhook = api.ParseWebhook(signature, payload);
        if (webhook.Transaction is not { } transaction)
        {
            return null;
        }

        // Braintree notifications have no id of their own; kind, transaction and timestamp identify one.
        var eventId = $"{webhook.Kind}:{transaction.Id}:{webhook.Timestamp:O}";
        var paymentOrderId = Guid.TryParse(transaction.OrderId, out var parsed) ? parsed : (Guid?)null;
        return new WebhookEvent(eventId, paymentOrderId, ToResult(transaction));
    }

    private string MerchantAccountFor(string? currency) =>
        _merchantAccounts.TryGetValue(RequestGuard.Currency(Kind, currency), out var merchantAccountId)
            ? merchantAccountId
            : throw new PaymentProviderException(
                Kind, PaymentProviderErrorKind.InvalidRequest, $"No Braintree merchant account is configured for {currency}.");

    private PaymentResult ToResult(BraintreeTransaction transaction) =>
        new(transaction.Id, ToStatus(transaction), transaction.FailureReason);

    private PaymentStatus ToStatus(BraintreeTransaction transaction) =>
        transaction.Status switch
        {
            BraintreeTransactionStatus.Authorizing or BraintreeTransactionStatus.Authorized => PaymentStatus.Authorized,

            // Card settlement is batched nightly and never pushed, so a captured sale counts as paid; a
            // later settlement decline surfaces through LookupAsync.
            BraintreeTransactionStatus.SubmittedForSettlement
                or BraintreeTransactionStatus.Settling
                or BraintreeTransactionStatus.SettlementConfirmed
                or BraintreeTransactionStatus.Settled => PaymentStatus.Succeeded,

            BraintreeTransactionStatus.SettlementPending => PaymentStatus.Processing,

            BraintreeTransactionStatus.ProcessorDeclined
                or BraintreeTransactionStatus.GatewayRejected
                or BraintreeTransactionStatus.Failed
                or BraintreeTransactionStatus.SettlementDeclined => PaymentStatus.Failed,

            BraintreeTransactionStatus.Voided or BraintreeTransactionStatus.AuthorizationExpired => PaymentStatus.Canceled,

            _ => throw new PaymentProviderException(
                Kind, PaymentProviderErrorKind.Unknown, $"Transaction {transaction.Id} has an unrecognised status."),
        };
}
