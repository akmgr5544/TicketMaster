namespace PaymentProvider.Models;

public sealed record CheckoutRequest(Guid PaymentOrderId, decimal Amount, string Currency);
