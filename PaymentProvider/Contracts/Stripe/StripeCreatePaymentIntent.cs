namespace PaymentProvider.Contracts.Stripe;

internal sealed record StripeCreatePaymentIntent(long Amount, string Currency, string PaymentOrderId, string IdempotencyKey);
