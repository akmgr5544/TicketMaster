namespace PaymentProvider.Models;

// Amount is explicit rather than implied "everything", so a refund may be partial. RefundId names this refund
// among the order's parts, and is what makes a repeat of it replay instead of refunding again. Without a
// ProviderReference the payment is found by PaymentOrderId, as LookupAsync finds it.
public sealed record RefundRequest(
    Guid PaymentOrderId, string? ProviderReference, decimal Amount, string Currency, Guid RefundId);
