namespace PaymentSystem.Shared.Messaging;

// A PSP's final answer about an order, from a webhook or a synchronous charge. Invoked inline, never queued.
public sealed record RecordPaymentOutcome(Guid PaymentOrderId, bool Succeeded, string ProviderReference);
