namespace PaymentProvider.Models;

public sealed record RefundLookupRequest(Guid PaymentOrderId, string ProviderReference);
