using PaymentProvider.Contracts.Braintree;

namespace PaymentProvider.Providers.Braintree;

internal interface IBraintreeApi
{
    Task<string> GenerateClientTokenAsync(string merchantAccountId);

    Task<BraintreeTransaction> SaleAsync(BraintreeSale sale);

    Task<BraintreeTransaction?> FindAsync(string transactionId);

    Task<BraintreeTransaction?> FindLatestByOrderIdAsync(string orderId);

    Task<BraintreeTransaction> RefundAsync(string transactionId, decimal amount);

    Task<BraintreeTransaction> VoidAsync(string transactionId);

    BraintreeWebhook ParseWebhook(string signature, string payload);
}
