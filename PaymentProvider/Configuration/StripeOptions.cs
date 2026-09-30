namespace PaymentProvider.Configuration;

internal sealed class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    // Null in every real deployment (Stripe's own API). Set only to aim the SDK at a local stand-in.
    public string? ApiBase { get; set; }
}
