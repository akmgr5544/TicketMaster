using System.Globalization;
using System.Security;

namespace PaymentAdapters.Fixtures;

// Braintree's XML wire format, written independently of the adapter so a test cannot agree with a bug.
public static class BraintreeWire
{
    public static string Transaction(
        string id = "tx_1",
        string status = "submitted_for_settlement",
        string? orderId = null,
        DateTime? createdAt = null,
        string? processorResponseText = null,
        string? gatewayRejectionReason = null,
        string? settlementResponseText = null,
        string type = "sale",
        params string[] refundIds)
    {
        var created = (createdAt ?? new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc))
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        return $"""
            <transaction>
              <id>{id}</id>
              <status>{status}</status>
              <type>{type}</type>
              <refund-ids type="array">{string.Concat(refundIds.Select(refundId => $"<item>{refundId}</item>"))}</refund-ids>
              <amount>10.00</amount>
              <currency-iso-code>EUR</currency-iso-code>
              {Element("order-id", orderId)}
              <created-at type="datetime">{created}</created-at>
              <updated-at type="datetime">{created}</updated-at>
              {Element("processor-response-text", processorResponseText)}
              {Element("gateway-rejection-reason", gatewayRejectionReason)}
              {Element("processor-settlement-response-text", settlementResponseText)}
            </transaction>
            """;
    }

    public static string ErrorResponse(string message, string? transaction = null, string? transactionErrors = null) =>
        $"""
        <api-error-response>
          <errors>
            <errors type="array"/>
            {(transactionErrors is null ? "" : $"<transaction><errors type=\"array\">{transactionErrors}</errors></transaction>")}
          </errors>
          <params><transaction><amount>10.00</amount><type>sale</type></transaction></params>
          <message>{SecurityElement.Escape(message)}</message>
          {transaction}
        </api-error-response>
        """;

    public static string ValidationError(string code, string attribute, string message) =>
        $"<error><code>{code}</code><attribute>{attribute}</attribute><message>{SecurityElement.Escape(message)}</message></error>";

    public static string ClientToken(string value) => $"<client-token><value>{value}</value></client-token>";

    public static string SearchIds(params string[] ids) =>
        $"""<search-results><page-size type="integer">50</page-size><ids type="array">{string.Concat(ids.Select(id => $"<item>{id}</item>"))}</ids></search-results>""";

    public static string SearchPage(params string[] transactions) =>
        $"""
        <credit-card-transactions type="collection">
          <current-page-number type="integer">1</current-page-number>
          <page-size type="integer">50</page-size>
          <total-items type="integer">{transactions.Length}</total-items>
          {string.Concat(transactions)}
        </credit-card-transactions>
        """;

    public static FakeResponse Xml(int status, string body) =>
        new(status, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + body, "application/xml; charset=utf-8");

    public static string MerchantPath(string resource) => $"/merchants/{Gateways.BraintreeMerchantId}/{resource}";

    private static string Element(string name, string? value) =>
        value is null ? $"<{name} nil=\"true\"/>" : $"<{name}>{SecurityElement.Escape(value)}</{name}>";
}
