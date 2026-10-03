using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stripe;

namespace PaymentAdapters.Fixtures;

// Stripe's wire format, written independently of the adapter so a test cannot agree with a bug.
public static class StripeWire
{
    public const string OrderIdKey = "payment_order_id";

    private static readonly IReadOnlyDictionary<string, string> NoRetry =
        new Dictionary<string, string> { ["Stripe-Should-Retry"] = "false" };

    public static string Intent(
        string id = "pi_1",
        string status = "requires_payment_method",
        Guid? orderId = null,
        string? rawOrderId = null,
        string? lastError = null,
        long created = 1_700_000_000,
        string? clientSecret = null)
    {
        var metadata = new Dictionary<string, string>();
        if ((rawOrderId ?? orderId?.ToString()) is { } value)
        {
            metadata[OrderIdKey] = value;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["object"] = "payment_intent",
            ["amount"] = 1099,
            ["currency"] = "usd",
            ["status"] = status,
            ["client_secret"] = clientSecret ?? $"{id}_secret_abc",
            ["created"] = created,
            ["livemode"] = false,
            ["metadata"] = metadata,
            ["last_payment_error"] = lastError is null
                ? null
                : new Dictionary<string, object?> { ["type"] = "card_error", ["code"] = "card_declined", ["message"] = lastError },
        });
    }

    public static string Refund(string id = "re_1", string status = "succeeded", long created = 1_700_000_100,
        string? failureReason = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["object"] = "refund",
            ["amount"] = 1099,
            ["currency"] = "usd",
            ["payment_intent"] = "pi_1",
            ["status"] = status,
            ["created"] = created,
            ["failure_reason"] = failureReason,
        });

    public static string List(params string[] items) =>
        $$"""{"object":"list","url":"/v1/refunds","has_more":false,"data":[{{string.Join(",", items)}}]}""";

    public static string Search(params string[] intents) =>
        $$"""{"object":"search_result","url":"/v1/payment_intents/search","has_more":false,"data":[{{string.Join(",", intents)}}]}""";

    public static string Event(string type, string dataObject, string id = "evt_1", string? apiVersion = null) =>
        $$"""{"id":"{{id}}","object":"event","api_version":"{{apiVersion ?? StripeConfiguration.ApiVersion}}","created":1700000000,"livemode":false,"type":"{{type}}","data":{"object":""" +
        dataObject + "}}";

    public static string Charge() => """{"id":"ch_1","object":"charge","amount":1099,"currency":"usd"}""";

    public static FakeResponse Ok(string json) => new(200, json, "application/json");

    public static FakeResponse Error(int status, string type, string? code = null, string message = "stripe says no") =>
        new(status, JsonSerializer.Serialize(new { error = new { type, code, message } }), "application/json", NoRetry);

    public static string Sign(string body, string secret, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds();
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(signature)}";
    }
}
