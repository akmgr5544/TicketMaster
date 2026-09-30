namespace PaymentProvider.Models;

// Providers deliver at least once and out of order: dedupe on EventId, and don't let an older
// event overwrite a newer status.
public sealed record WebhookEvent(string EventId, Guid? PaymentOrderId, PaymentResult Payment);
