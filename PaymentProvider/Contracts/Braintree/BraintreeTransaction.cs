namespace PaymentProvider.Contracts.Braintree;

internal sealed record BraintreeTransaction(string Id, BraintreeTransactionStatus Status, string? OrderId, string? FailureReason);
