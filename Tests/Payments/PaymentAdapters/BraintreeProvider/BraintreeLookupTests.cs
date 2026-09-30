using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.BraintreeProvider;

public sealed class BraintreeLookupTests : IDisposable
{
    private readonly FakeHttpServer _braintree = new();

    public void Dispose() => _braintree.Dispose();

    [Theory]
    [InlineData("authorizing", PaymentStatus.Authorized)]
    [InlineData("authorized", PaymentStatus.Authorized)]
    [InlineData("submitted_for_settlement", PaymentStatus.Succeeded)]
    [InlineData("settling", PaymentStatus.Succeeded)]
    [InlineData("settlement_confirmed", PaymentStatus.Succeeded)]
    [InlineData("settled", PaymentStatus.Succeeded)]
    [InlineData("settlement_pending", PaymentStatus.Processing)]
    [InlineData("processor_declined", PaymentStatus.Failed)]
    [InlineData("gateway_rejected", PaymentStatus.Failed)]
    [InlineData("failed", PaymentStatus.Failed)]
    [InlineData("settlement_declined", PaymentStatus.Failed)]
    [InlineData("voided", PaymentStatus.Canceled)]
    [InlineData("authorization_expired", PaymentStatus.Canceled)]
    public async Task Maps_every_transaction_status(string braintreeStatus, PaymentStatus expected)
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.Transaction(status: braintreeStatus)));

        var result = await Lookup("tx_1");

        Assert.Equal(expected, result!.Status);
    }

    [Fact]
    public async Task A_status_Braintree_adds_later_is_surfaced_not_guessed()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "something_new")));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Lookup("tx_1"));

        Assert.Equal(PaymentProviderErrorKind.Unknown, exception.Kind);
    }

    [Fact]
    public async Task A_settlement_decline_found_later_carries_the_settlement_reason()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.Transaction(
            status: "settlement_declined", processorResponseText: "Approved", settlementResponseText: "Insufficient funds")));

        var result = await Lookup("tx_1");

        Assert.Equal(new PaymentResult("tx_1", PaymentStatus.Failed, "Insufficient funds"), result);
    }

    [Fact]
    public async Task Reads_by_reference_when_it_has_one()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.Transaction(id: "tx_42")));

        await Lookup("tx_42");

        var request = Assert.Single(_braintree.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(BraintreeWire.MerchantPath("transactions/tx_42"), request.Path);
    }

    [Fact]
    public async Task A_transaction_Braintree_does_not_know_is_no_payment_rather_than_an_error()
    {
        _braintree.Respond(_ => new FakeResponse(404, "", "application/xml"));

        Assert.Null(await Lookup("tx_missing"));
    }

    [Fact]
    public async Task Without_a_reference_searches_by_the_order_id_the_sale_was_tagged_with()
    {
        var orderId = Guid.NewGuid();
        _braintree.Respond(request => request.Path.EndsWith("advanced_search_ids", StringComparison.Ordinal)
            ? BraintreeWire.Xml(200, BraintreeWire.SearchIds("tx_1"))
            : BraintreeWire.Xml(200, BraintreeWire.SearchPage(BraintreeWire.Transaction(orderId: orderId.ToString()))));

        var result = await Gateways.Braintree(_braintree).LookupAsync(new PaymentLookupRequest(orderId, null));

        Assert.Equal("tx_1", result!.ProviderReference);
        var search = _braintree.Requests[0];
        Assert.Contains($"<is>{orderId}</is>", search.Body);
        Assert.Contains("<order-id>", search.Body);
    }

    [Fact]
    public async Task When_a_search_finds_several_attempts_the_newest_one_wins_whatever_order_they_come_in()
    {
        var orderId = Guid.NewGuid();
        var first = new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
        _braintree.Respond(request => request.Path.EndsWith("advanced_search_ids", StringComparison.Ordinal)
            ? BraintreeWire.Xml(200, BraintreeWire.SearchIds("tx_old", "tx_new", "tx_mid"))
            : BraintreeWire.Xml(200, BraintreeWire.SearchPage(
                BraintreeWire.Transaction(id: "tx_old", status: "processor_declined", createdAt: first),
                BraintreeWire.Transaction(id: "tx_new", status: "settled", createdAt: first.AddMinutes(10)),
                BraintreeWire.Transaction(id: "tx_mid", status: "processor_declined", createdAt: first.AddMinutes(5)))));

        var result = await Gateways.Braintree(_braintree).LookupAsync(new PaymentLookupRequest(orderId, null));

        Assert.Equal("tx_new", result!.ProviderReference);
        Assert.Equal(PaymentStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_is_no_payment()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.SearchIds()));

        Assert.Null(await Gateways.Braintree(_braintree).LookupAsync(new PaymentLookupRequest(Guid.NewGuid(), null)));
    }

    private Task<PaymentResult?> Lookup(string reference) =>
        Gateways.Braintree(_braintree).LookupAsync(new PaymentLookupRequest(Guid.NewGuid(), reference));
}
