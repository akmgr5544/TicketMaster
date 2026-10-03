namespace PaymentProvider.Models;

// Amount is explicit rather than implied "everything", so a partial refund needs no new contract. Without a
// ProviderReference the payment is found by PaymentOrderId, as LookupAsync finds it.
public sealed record RefundRequest(Guid PaymentOrderId, string? ProviderReference, decimal Amount, string Currency);
