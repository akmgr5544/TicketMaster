namespace PaymentProvider.Contracts.Stripe;

// A balance transaction with its source read: a charge is named by its payment intent, a refund by its own id. Amount
// and currency are the source's — the balance transaction's own are in the settlement currency, after conversion.
// Null fields are for a type whose source is neither a charge nor a refund.
internal sealed record StripeBalanceTransaction(
    string Id,
    string Type,
    string? SourceReference,
    long? Amount,
    string? Currency,
    string? PaymentOrderId,
    string? RefundId);
