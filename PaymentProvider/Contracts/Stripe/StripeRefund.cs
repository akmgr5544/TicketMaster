namespace PaymentProvider.Contracts.Stripe;

internal sealed record StripeCreateRefund(
    string PaymentIntentId, long Amount, string PaymentOrderId, string RefundId, string IdempotencyKey);

// PaymentOrderId and RefundId are the metadata written when the refund was created.
internal sealed record StripeRefund(
    string Id, StripeRefundStatus Status, string? FailureReason, string? PaymentOrderId = null, string? RefundId = null);

internal enum StripeRefundStatus
{
    Pending,
    RequiresAction,
    Succeeded,
    Failed,
    Canceled,
    Unknown,
}
