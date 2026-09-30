namespace PaymentProvider.Contracts.Stripe;

internal sealed record StripeEvent(string Id, string Type, StripePaymentIntent? PaymentIntent);
