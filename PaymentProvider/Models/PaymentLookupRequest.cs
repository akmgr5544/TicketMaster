namespace PaymentProvider.Models;

// Without a ProviderReference the provider is searched by PaymentOrderId, which is how a payment
// created by a request that timed out is recovered.
public sealed record PaymentLookupRequest(Guid PaymentOrderId, string? ProviderReference);
