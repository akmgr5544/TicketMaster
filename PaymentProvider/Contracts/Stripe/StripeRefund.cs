namespace PaymentProvider.Contracts.Stripe;

internal sealed record StripeCreateRefund(
    string PaymentIntentId, long Amount, string PaymentOrderId, string RefundId, string IdempotencyKey);

internal sealed record StripeRefund(string Id, StripeRefundStatus Status, string? FailureReason);

internal enum StripeRefundStatus
{
    Pending,
    RequiresAction,
    Succeeded,
    Failed,
    Canceled,
    Unknown,
}
