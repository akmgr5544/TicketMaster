namespace PaymentProvider.Contracts.Braintree;

// RefundIds are the credit transactions already issued against this sale: how a repeated refund request
// finds the first, by the refund id it carried as its OrderId, since Braintree has no idempotency key.
internal sealed record BraintreeTransaction(
    string Id,
    BraintreeTransactionStatus Status,
    string? OrderId,
    string? FailureReason,
    IReadOnlyList<string>? RefundIds = null,
    decimal? Amount = null,
    string? Currency = null,
    BraintreeTransactionType Type = BraintreeTransactionType.Sale);

internal enum BraintreeTransactionType
{
    Sale,
    // A refund: Braintree issues one as a credit transaction against the sale.
    Credit,
}
