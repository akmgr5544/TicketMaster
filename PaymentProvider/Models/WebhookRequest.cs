namespace PaymentProvider.Models;

// Body must be the raw request body: signatures are computed over the exact bytes received.
public sealed record WebhookRequest(string Body, IReadOnlyDictionary<string, string> Headers)
{
    public string? GetHeader(string name) =>
        Headers.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
