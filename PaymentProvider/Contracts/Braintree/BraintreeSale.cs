namespace PaymentProvider.Contracts.Braintree;

internal sealed record BraintreeSale(
    decimal Amount,
    string PaymentMethodNonce,
    string OrderId,
    string MerchantAccountId,
    bool SubmitForSettlement);
