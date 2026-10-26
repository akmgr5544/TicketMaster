using PaymentProvider.Contracts.Braintree;

namespace PaymentProvider.Providers.Braintree;

internal interface IBraintreeApi
{
    Task<string> GenerateClientTokenAsync(string merchantAccountId);

    Task<BraintreeTransaction> SaleAsync(BraintreeSale sale);

    Task<BraintreeTransaction?> FindAsync(string transactionId);

    Task<BraintreeTransaction?> FindLatestByOrderIdAsync(string orderId);

    Task<IReadOnlyList<BraintreeTransaction>> SearchSettledAsync(DateTime from, DateTime to);

    Task<IReadOnlyList<BraintreeTransaction>> SearchDeclinedCreditsAsync(DateTime submittedFrom, DateTime submittedTo);

    Task<BraintreeTransaction> RefundAsync(string transactionId, decimal amount, string orderId);

    Task<BraintreeTransaction> VoidAsync(string transactionId);

    BraintreeWebhook ParseWebhook(string signature, string payload);
}
