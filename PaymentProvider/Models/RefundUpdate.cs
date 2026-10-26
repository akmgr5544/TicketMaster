namespace PaymentProvider.Models;

// RefundId is the id the refund was requested under, when the provider carries it back.
public sealed record RefundUpdate(Guid? RefundId, RefundResult Result);
