namespace PaymentProvider.Models;

public sealed record PaymentResult(string ProviderReference, PaymentStatus Status, string? FailureReason = null);
