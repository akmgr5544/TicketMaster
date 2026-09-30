namespace PaymentProvider.Models;

// ProviderReference is null for a provider that creates nothing until a payment method is
// submitted (Braintree). ClientToken goes to the client's payment form and must not be logged.
public sealed record CheckoutSession(string? ProviderReference, string ClientToken);
