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

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default)
    {
        RequestGuard.PaymentOrderId(Kind, request.PaymentOrderId);
        RequestGuard.Amount(Kind, request.Amount);

        var sale = request.ProviderReference is { } reference
            ? await api.FindAsync(reference)
            : await api.FindLatestByOrderIdAsync(request.PaymentOrderId.ToString());
        if (sale is null)
            throw RequestGuard.Invalid(Kind, $"Payment order {request.PaymentOrderId} has no payment to refund.");

        // Braintree has no idempotency key, so a repeated request is answered from what the sale already shows:
        // each refund carries its refund id as its order id, and an earlier part of the order is not this one.
        var refundId = request.RefundId.ToString();
        if (sale.Status == BraintreeTransactionStatus.Voided)
            return new RefundResult(sale.Id, RefundStatus.Succeeded);
        foreach (var issuedId in sale.RefundIds ?? [])
        {
            if (await api.FindAsync(issuedId) is { } issued && issued.OrderId == refundId)
                return ToRefundResult(issued);
        }

        // An unsettled sale cannot be refunded, only voided — which returns all of it, so only a full refund
        // may take that path.
        switch (sale.Status)
        {
            case BraintreeTransactionStatus.Authorizing
                or BraintreeTransactionStatus.Authorized
                or BraintreeTransactionStatus.SubmittedForSettlement:
                if (request.Amount != sale.Amount)
                    throw RequestGuard.Invalid(Kind,
                        $"Sale {sale.Id} has not settled, so it can only be voided in full, not refunded in part.");
                var voided = await api.VoidAsync(sale.Id);
                return voided.Status == BraintreeTransactionStatus.Voided
                    ? new RefundResult(voided.Id, RefundStatus.Succeeded)
                    : new RefundResult(voided.Id, RefundStatus.Failed, voided.FailureReason);

            case BraintreeTransactionStatus.Settling
                or BraintreeTransactionStatus.SettlementConfirmed
                or BraintreeTransactionStatus.Settled:
                return ToRefundResult(await api.RefundAsync(sale.Id, request.Amount, refundId));

            default:
                throw RequestGuard.Invalid(Kind,
                    $"Payment order {request.PaymentOrderId} has no successful payment to refund (sale is {sale.Status}).");
        }
    }

    public async Task<RefundResult?> LookupRefundAsync(RefundLookupRequest request, CancellationToken cancellationToken = default)
    {
        // A void answers a refund of an unsettled sale with the sale's own id, and a voided sale stays voided.
        var transaction = await api.FindAsync(request.ProviderReference);
        if (transaction is null)
            return null;

        return transaction.Status == BraintreeTransactionStatus.Voided
            ? new RefundResult(transaction.Id, RefundStatus.Succeeded)
            : ToRefundResult(transaction);
    }

    public async Task<IReadOnlyList<SettlementLine>> GetSettlementAsync(SettlementRequest request,
        CancellationToken cancellationToken = default)
    {
        var settled = await api.SearchSettledAsync(request.From, request.To);
        var declinedCredits = await api.SearchDeclinedCreditsAsync(request.From, request.To);

        // A sale carries the payment order id as its order id, a credit the refund id it was issued under.
        var lines = settled
            .Select(transaction => transaction.Type == BraintreeTransactionType.Credit
                ? ToSettlementLine(transaction, SettlementLineKind.Refund, refundId: ParseGuid(transaction.OrderId))
                : ToSettlementLine(transaction, SettlementLineKind.Charge, paymentOrderId: ParseGuid(transaction.OrderId)))
            .Concat(declinedCredits.Select(credit =>
                ToSettlementLine(credit, SettlementLineKind.RefundFailure, refundId: ParseGuid(credit.OrderId))));

        return lines.OfType<SettlementLine>().ToArray();
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

    // Null for a transaction Braintree returned without an amount or currency, which a settled one always has.
    private static SettlementLine? ToSettlementLine(BraintreeTransaction transaction, SettlementLineKind kind,
        Guid? paymentOrderId = null, Guid? refundId = null) =>
        transaction is { Amount: { } amount, Currency: { } currency }
            ? new SettlementLine(transaction.Id, kind, amount, currency.ToUpperInvariant(), paymentOrderId, refundId)
            : null;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;

    private string MerchantAccountFor(string? currency) =>
        _merchantAccounts.TryGetValue(RequestGuard.Currency(Kind, currency), out var merchantAccountId)
            ? merchantAccountId
            : throw new PaymentProviderException(
                Kind, PaymentProviderErrorKind.InvalidRequest, $"No Braintree merchant account is configured for {currency}.");

    // A refund is a credit transaction with the same lifecycle as a sale, and is treated the same way: once
    // submitted for settlement it counts as done.
    private RefundResult ToRefundResult(BraintreeTransaction refund) =>
        ToStatus(refund) switch
        {
            PaymentStatus.Succeeded => new RefundResult(refund.Id, RefundStatus.Succeeded),
            PaymentStatus.Failed or PaymentStatus.Canceled => new RefundResult(refund.Id, RefundStatus.Failed, refund.FailureReason),
            _ => new RefundResult(refund.Id, RefundStatus.Pending),
        };

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
