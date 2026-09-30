namespace PaymentProvider.Configuration;

internal sealed class BraintreeOptions
{
    public string Environment { get; set; } = "sandbox";

    public string MerchantId { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public string PrivateKey { get; set; } = string.Empty;

    // Null in every real deployment, where Environment picks Braintree's URL. Set only to aim the
    // SDK at a local stand-in.
    public string? GatewayUrl { get; set; }

    // Braintree takes no currency on a sale; the merchant account decides it, so each supported
    // currency (ISO 4217) needs its own account.
    public Dictionary<string, string> MerchantAccounts { get; set; } = [];
}
