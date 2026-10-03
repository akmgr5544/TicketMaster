using System.Xml;
using Braintree;
using Braintree.Exceptions;
using Microsoft.Extensions.Options;
using PaymentProvider.Configuration;
using PaymentProvider.Contracts.Braintree;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using BraintreeEnvironment = Braintree.Environment;

namespace PaymentProvider.Providers.Braintree;

// The only type that touches the Braintree SDK: SDK objects and exceptions are translated here so
// the adapter works purely with the internal contracts. The SDK takes no CancellationToken.
internal sealed class BraintreeApi(IOptions<BraintreeOptions> options) : IBraintreeApi
{
    private readonly BraintreeGateway _gateway = new(
        options.Value.GatewayUrl is { } gatewayUrl
            ? new BraintreeEnvironment("development", gatewayUrl, gatewayUrl, gatewayUrl)
            : BraintreeEnvironment.ParseEnvironment(options.Value.Environment),
        options.Value.MerchantId,
        options.Value.PublicKey,
        options.Value.PrivateKey);

    public Task<string> GenerateClientTokenAsync(string merchantAccountId) =>
        CallAsync(() => _gateway.ClientToken.GenerateAsync(new ClientTokenRequest { MerchantAccountId = merchantAccountId }));

    public async Task<BraintreeTransaction> SaleAsync(BraintreeSale sale)
    {
        var request = new TransactionRequest
        {
            Amount = sale.Amount,
            PaymentMethodNonce = sale.PaymentMethodNonce,
            OrderId = sale.OrderId,
            MerchantAccountId = sale.MerchantAccountId,
            Options = new TransactionOptionsRequest { SubmitForSettlement = sale.SubmitForSettlement },
        };

        var result = await CallAsync(() => _gateway.Transaction.SaleAsync(request));
        if (result.IsSuccess())
        {
            return ToContract(result.Target);
        }

        // A decline or rejection still creates a transaction; only a validation error (such as a
        // consumed or invalid nonce) comes back without one.
        return result.Transaction is { } declined
            ? ToContract(declined)
            : throw new PaymentProviderException(
                PaymentProviderKind.Braintree, PaymentProviderErrorKind.InvalidRequest, result.Message);
    }

    public async Task<BraintreeTransaction?> FindAsync(string transactionId)
    {
        try
        {
            return ToContract(await CallAsync(() => _gateway.Transaction.FindAsync(transactionId)));
        }
        catch (PaymentProviderException exception) when (exception.InnerException is NotFoundException)
        {
            return null;
        }
    }

    public async Task<BraintreeTransaction?> FindLatestByOrderIdAsync(string orderId)
    {
        var search = new TransactionSearchRequest().OrderId.Is(orderId);
        var transactions = await CallAsync(() => _gateway.Transaction.SearchAsync(search));

        // Each declined attempt leaves its own transaction under the same order id.
        var latest = transactions.MaxBy(transaction => transaction.CreatedAt);
        return latest is null ? null : ToContract(latest);
    }

    public async Task<BraintreeTransaction> RefundAsync(string transactionId, decimal amount, string orderId) =>
        ToContract(Outcome(await CallAsync(() => _gateway.Transaction.RefundAsync(transactionId,
            new TransactionRefundRequest { Amount = amount, OrderId = orderId }))));

    public async Task<BraintreeTransaction> VoidAsync(string transactionId) =>
        ToContract(Outcome(await CallAsync(() => _gateway.Transaction.VoidAsync(transactionId))));

    public BraintreeWebhook ParseWebhook(string signature, string payload)
    {
        WebhookNotification notification;
        try
        {
            notification = _gateway.WebhookNotification.Parse(signature, payload);
        }
        catch (InvalidSignatureException exception)
        {
            throw new PaymentProviderException(
                PaymentProviderKind.Braintree, PaymentProviderErrorKind.InvalidSignature, exception.Message, exception);
        }

        var transaction = notification.Transaction is null ? null : ToContract(notification.Transaction);
        return new BraintreeWebhook(notification.Kind.ToString(), notification.Timestamp, transaction);
    }

    private static BraintreeTransaction ToContract(Transaction transaction)
    {
        var status = ToStatus(transaction.Status);
        var failureReason = status switch
        {
            BraintreeTransactionStatus.GatewayRejected => transaction.GatewayRejectionReason.ToString(),
            BraintreeTransactionStatus.SettlementDeclined => transaction.ProcessorSettlementResponseText,
            BraintreeTransactionStatus.ProcessorDeclined or BraintreeTransactionStatus.Failed => transaction.ProcessorResponseText,
            _ => null,
        };

        return new BraintreeTransaction(transaction.Id, status, transaction.OrderId, failureReason, transaction.RefundIds,
            transaction.Amount);
    }

    // A refused refund or void comes back without a transaction; the message says why (already refunded,
    // amount too large, wrong status).
    private static Transaction Outcome(Result<Transaction> result) =>
        result.IsSuccess()
            ? result.Target
            : result.Transaction
              ?? throw new PaymentProviderException(
                  PaymentProviderKind.Braintree, PaymentProviderErrorKind.InvalidRequest, result.Message);

    private static BraintreeTransactionStatus ToStatus(TransactionStatus status) =>
        status switch
        {
            TransactionStatus.AUTHORIZING => BraintreeTransactionStatus.Authorizing,
            TransactionStatus.AUTHORIZED => BraintreeTransactionStatus.Authorized,
            TransactionStatus.SUBMITTED_FOR_SETTLEMENT => BraintreeTransactionStatus.SubmittedForSettlement,
            TransactionStatus.SETTLING => BraintreeTransactionStatus.Settling,
            TransactionStatus.SETTLEMENT_PENDING => BraintreeTransactionStatus.SettlementPending,
            TransactionStatus.SETTLEMENT_CONFIRMED => BraintreeTransactionStatus.SettlementConfirmed,
            TransactionStatus.SETTLED => BraintreeTransactionStatus.Settled,
            TransactionStatus.SETTLEMENT_DECLINED => BraintreeTransactionStatus.SettlementDeclined,
            TransactionStatus.PROCESSOR_DECLINED => BraintreeTransactionStatus.ProcessorDeclined,
            TransactionStatus.GATEWAY_REJECTED => BraintreeTransactionStatus.GatewayRejected,
            TransactionStatus.FAILED => BraintreeTransactionStatus.Failed,
            TransactionStatus.VOIDED => BraintreeTransactionStatus.Voided,
            TransactionStatus.AUTHORIZATION_EXPIRED => BraintreeTransactionStatus.AuthorizationExpired,
            _ => BraintreeTransactionStatus.Unknown,
        };

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (BraintreeException exception)
        {
            throw new PaymentProviderException(PaymentProviderKind.Braintree, ToErrorKind(exception), exception.Message, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PaymentProviderException(
                PaymentProviderKind.Braintree, PaymentProviderErrorKind.Transient, "Braintree could not be reached.", exception);
        }
        catch (XmlException exception)
        {
            throw new PaymentProviderException(
                PaymentProviderKind.Braintree, PaymentProviderErrorKind.Unknown, "Braintree's response was not XML.", exception);
        }
    }

    private static PaymentProviderErrorKind ToErrorKind(BraintreeException exception) =>
        exception switch
        {
            ServerException or ServiceUnavailableException or TooManyRequestsException
                or GatewayTimeoutException or RequestTimeoutException => PaymentProviderErrorKind.Transient,
            // 426: Braintree no longer accepts this SDK version.
            AuthenticationException or AuthorizationException or UpgradeRequiredException or ConfigurationException
                => PaymentProviderErrorKind.Configuration,
            NotFoundException => PaymentProviderErrorKind.InvalidRequest,
            _ => PaymentProviderErrorKind.Unknown,
        };
}
