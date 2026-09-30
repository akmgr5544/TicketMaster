namespace PaymentProvider.Contracts.Braintree;

internal sealed record BraintreeWebhook(string Kind, DateTime? Timestamp, BraintreeTransaction? Transaction);
