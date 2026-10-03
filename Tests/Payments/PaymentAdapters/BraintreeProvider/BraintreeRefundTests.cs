using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.BraintreeProvider;

public sealed class BraintreeRefundTests : IDisposable
{
    private readonly FakeHttpServer _braintree = new();

    public void Dispose() => _braintree.Dispose();

    [Fact]
    public async Task A_settled_sale_is_refunded_and_the_credit_counts_once_submitted()
    {
        _braintree.Respond(request => request.Method == "GET"
            ? BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "settled"))
            : BraintreeWire.Xml(201, BraintreeWire.Transaction(id: "tx_refund", status: "submitted_for_settlement", type: "credit")));

        var refundId = Guid.NewGuid();

        var result = await Refund(10.00m, refundId);

        Assert.Equal(new RefundResult("tx_refund", RefundStatus.Succeeded), result);
        var refund = _braintree.Requests[1];
        Assert.Equal("POST", refund.Method);
        Assert.Equal(BraintreeWire.MerchantPath("transactions/tx_1/refund"), refund.Path);
        Assert.Contains("<amount>10.00</amount>", refund.Body);
        // Braintree has no idempotency key, so the refund carries its id to be recognised by on a repeat.
        Assert.Contains($"<order-id>{refundId}</order-id>", refund.Body);
    }

    // An unsettled sale cannot be refunded, only voided.
    [Fact]
    public async Task An_unsettled_sale_is_voided_instead()
    {
        _braintree.Respond(request => request.Method == "GET"
            ? BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "submitted_for_settlement"))
            : BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "voided")));

        var result = await Refund(10.00m);

        Assert.Equal(new RefundResult("tx_1", RefundStatus.Succeeded), result);
        var voided = _braintree.Requests[1];
        Assert.Equal("PUT", voided.Method);
        Assert.Equal(BraintreeWire.MerchantPath("transactions/tx_1/void"), voided.Path);
    }

    // A void returns everything, so it cannot stand in for a partial refund.
    [Fact]
    public async Task A_partial_refund_of_an_unsettled_sale_is_refused_rather_than_voiding_all_of_it()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "submitted_for_settlement")));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Refund(4.00m));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Single(_braintree.Requests);
    }

    // Braintree has no idempotency key: a repeated request must be answered from the sale, not by a second refund.
    [Fact]
    public async Task A_sale_already_voided_is_reported_refunded_without_another_call()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "voided")));

        var result = await Refund(10.00m);

        Assert.Equal(new RefundResult("tx_1", RefundStatus.Succeeded), result);
        Assert.Single(_braintree.Requests);
    }

    [Fact]
    public async Task A_refund_already_issued_is_reported_instead_of_issuing_another()
    {
        var refundId = Guid.NewGuid();
        _braintree.Respond(request => request.Path.EndsWith("tx_refund", StringComparison.Ordinal)
            ? BraintreeWire.Xml(200, BraintreeWire.Transaction(id: "tx_refund", status: "settled",
                orderId: refundId.ToString(), type: "credit"))
            : BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "settled", refundIds: "tx_refund")));

        var result = await Refund(10.00m, refundId);

        Assert.Equal(new RefundResult("tx_refund", RefundStatus.Succeeded), result);
        Assert.All(_braintree.Requests, request => Assert.Equal("GET", request.Method));
    }

    // An earlier part of the same order is somebody else's refund, not an answer to this one.
    [Fact]
    public async Task An_earlier_refund_of_another_part_does_not_stand_in_for_this_one()
    {
        _braintree.Respond(request => (request.Method, request.Path) switch
        {
            ("GET", var path) when path.EndsWith("tx_earlier", StringComparison.Ordinal) =>
                BraintreeWire.Xml(200, BraintreeWire.Transaction(id: "tx_earlier", status: "settled",
                    orderId: Guid.NewGuid().ToString(), type: "credit")),
            ("GET", _) => BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "settled", refundIds: "tx_earlier")),
            _ => BraintreeWire.Xml(201, BraintreeWire.Transaction(id: "tx_refund", status: "submitted_for_settlement", type: "credit")),
        });

        var result = await Refund(4.00m, Guid.NewGuid());

        Assert.Equal(new RefundResult("tx_refund", RefundStatus.Succeeded), result);
        Assert.Equal("POST", _braintree.Requests[^1].Method);
    }

    [Fact]
    public async Task A_declined_sale_has_nothing_to_refund()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "processor_declined")));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Refund(10.00m));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
    }

    [Fact]
    public async Task A_refund_Braintree_refuses_is_an_invalid_request_with_its_message()
    {
        _braintree.Respond(request => request.Method == "GET"
            ? BraintreeWire.Xml(200, BraintreeWire.Transaction(status: "settled"))
            : BraintreeWire.Xml(422, BraintreeWire.ErrorResponse("Refund amount is too large.")));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Refund(10.00m));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Contains("Refund amount is too large.", exception.Message);
    }

    private Task<RefundResult> Refund(decimal amount, Guid? refundId = null) =>
        Gateways.Braintree(_braintree).RefundAsync(
            new RefundRequest(Guid.NewGuid(), "tx_1", amount, "EUR", refundId ?? Guid.NewGuid()));
}
