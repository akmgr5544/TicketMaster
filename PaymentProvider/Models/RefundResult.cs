namespace PaymentProvider.Models;

public sealed record RefundResult(string ProviderReference, RefundStatus Status, string? FailureReason = null);
