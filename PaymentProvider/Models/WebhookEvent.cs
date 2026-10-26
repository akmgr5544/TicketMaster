namespace PaymentProvider.Models;

// Providers deliver at least once and out of order: dedupe on EventId, and don't let an older
// event overwrite a newer status. Exactly one of Payment and Refund is set.
public sealed record WebhookEvent(string EventId, Guid? PaymentOrderId, PaymentResult? Payment, RefundUpdate? Refund = null);
