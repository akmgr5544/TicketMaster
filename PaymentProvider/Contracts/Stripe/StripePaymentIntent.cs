namespace PaymentProvider.Contracts.Stripe;

internal sealed record StripePaymentIntent(
    string Id,
    StripePaymentIntentStatus Status,
    string ClientSecret,
    string? PaymentOrderId,
    string? LastPaymentError);
