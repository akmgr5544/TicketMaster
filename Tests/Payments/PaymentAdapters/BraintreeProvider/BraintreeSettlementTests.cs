using PaymentAdapters.Fixtures;
using PaymentProvider.Models;

namespace PaymentAdapters.BraintreeProvider;

// What Braintree settled is its transactions searched by settlement date: a sale is a charge named by its own id, a
// credit a refund. A credit declined at settlement never settles, so it is searched for separately.
public sealed class BraintreeSettlementTests : IDisposable
{
    private static readonly DateTime From = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeHttpServer _braintree = new();

    public void Dispose() => _braintree.Dispose();

    private Task<IReadOnlyList<SettlementLine>> Settle() =>
        Gateways.Braintree(_braintree).GetSettlementAsync(new SettlementRequest(From, To));

    // The settled search names <settled-at>; the declined-credit one does not.
    private static bool IsSettledSearch(RecordedRequest request) => request.Body.Contains("<settled-at>");

    private void Respond(string[] settled, string[] declined) =>
        _braintree.Respond(request =>
        {
            var transactions = IsSettledSearch(request) ? settled : declined;
            return request.Path.EndsWith("advanced_search_ids", StringComparison.Ordinal)
                ? BraintreeWire.Xml(200, BraintreeWire.SearchIds(transactions.Select((_, i) => $"tx_{i}").ToArray()))
                : BraintreeWire.Xml(200, BraintreeWire.SearchPage(transactions));
        });

    [Fact]
    public async Task A_settled_sale_is_a_charge_named_by_its_id_with_the_order_it_was_tagged_with()
    {
        var orderId = Guid.NewGuid();
        Respond([BraintreeWire.Transaction(id: "bt_1", status: "settled", orderId: orderId.ToString())], []);

        var line = Assert.Single(await Settle());

        Assert.Equal(new SettlementLine("bt_1", SettlementLineKind.Charge, 10.00m, "EUR", orderId), line);
    }

    [Fact]
    public async Task A_settled_credit_is_a_refund_with_the_refund_id_it_was_issued_under()
    {
        var refundId = Guid.NewGuid();
        Respond([BraintreeWire.Transaction(id: "cr_1", status: "settled", orderId: refundId.ToString(), type: "credit")], []);

        var line = Assert.Single(await Settle());

        Assert.Equal(new SettlementLine("cr_1", SettlementLineKind.Refund, 10.00m, "EUR", null, refundId), line);
    }

    [Fact]
    public async Task A_credit_declined_at_settlement_is_a_refund_failure()
    {
        Respond([], [BraintreeWire.Transaction(id: "cr_2", status: "settlement_declined", type: "credit")]);

        var line = Assert.Single(await Settle());

        Assert.Equal(("cr_2", SettlementLineKind.RefundFailure), (line.ProviderReference, line.Kind));
    }

    [Fact]
    public async Task Searches_settlements_in_the_window_and_declined_credits_submitted_in_it()
    {
        Respond([], []);

        await Settle();

        var searches = _braintree.Requests.Where(r => r.Path.EndsWith("advanced_search_ids", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, searches.Count);
        var settled = Assert.Single(searches, IsSettledSearch);
        Assert.Contains("2026-03-01", settled.Body);
        Assert.Contains("2026-03-09", settled.Body);
        var declined = Assert.Single(searches, r => !IsSettledSearch(r));
        Assert.Contains("<submitted-for-settlement-at>", declined.Body);
        Assert.Contains("settlement_declined", declined.Body);
        Assert.Contains("credit", declined.Body);
    }
}
