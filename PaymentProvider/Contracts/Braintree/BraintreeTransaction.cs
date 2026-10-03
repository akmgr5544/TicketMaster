namespace PaymentProvider.Contracts.Braintree;

// RefundIds are the credit transactions already issued against this sale: how a second refund request
// finds the first, since Braintree has no idempotency key.
internal sealed record BraintreeTransaction(
    string Id,
    BraintreeTransactionStatus Status,
    string? OrderId,
    string? FailureReason,
    IReadOnlyList<string>? RefundIds = null,
    decimal? Amount = null);
