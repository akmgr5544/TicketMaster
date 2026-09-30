namespace PaymentSystem.Domain;

public sealed record PaymentOrderLine(Guid MerchantId, decimal Amount, string Currency);
