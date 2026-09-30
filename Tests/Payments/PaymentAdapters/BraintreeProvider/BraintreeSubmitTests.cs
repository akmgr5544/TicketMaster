using System.Globalization;
using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.BraintreeProvider;

public sealed class BraintreeSubmitTests : IDisposable
{
    private readonly FakeHttpServer _braintree = new();

    public void Dispose() => _braintree.Dispose();

    [Fact]
    public async Task Charges_the_nonce_for_the_order_on_its_currency_s_account_and_captures_immediately()
    {
        var orderId = Guid.NewGuid();
        _braintree.Respond(_ => BraintreeWire.Xml(201, BraintreeWire.Transaction()));

        await Submit(orderId, 10.5m, "USD", "fake-valid-nonce");

        var request = Assert.Single(_braintree.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(BraintreeWire.MerchantPath("transactions"), request.Path);
        Assert.Contains("<amount>10.50</amount>", request.Body);
        Assert.Contains("<payment-method-nonce>fake-valid-nonce</payment-method-nonce>", request.Body);
        Assert.Contains($"<order-id>{orderId}</order-id>", request.Body);
        Assert.Contains($"<merchant-account-id>{Gateways.UsdMerchantAccount}</merchant-account-id>", request.Body);
        Assert.Contains("<submit-for-settlement>true</submit-for-settlement>", request.Body);
    }

    [Fact]
    public async Task The_amount_on_the_wire_does_not_depend_on_the_server_s_culture()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(201, BraintreeWire.Transaction()));
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            await Submit(Guid.NewGuid(), 1234.56m, "EUR", "nonce");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Contains("<amount>1234.56</amount>", Assert.Single(_braintree.Requests).Body);
    }

    [Fact]
    public async Task A_captured_sale_is_a_success()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(201, BraintreeWire.Transaction(id: "tx_9", status: "submitted_for_settlement")));

        var result = await Submit(Guid.NewGuid(), 10m, "EUR", "nonce");

        Assert.Equal(new PaymentResult("tx_9", PaymentStatus.Succeeded), result);
    }

    [Fact]
    public async Task A_processor_decline_is_a_failed_attempt_with_the_processor_s_reason()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(422, BraintreeWire.ErrorResponse(
            "Do Not Honor",
            BraintreeWire.Transaction(id: "tx_d", status: "processor_declined", processorResponseText: "Do Not Honor"))));

        var result = await Submit(Guid.NewGuid(), 2000m, "EUR", "nonce");

        Assert.Equal(new PaymentResult("tx_d", PaymentStatus.Failed, "Do Not Honor"), result);
    }

    [Fact]
    public async Task A_gateway_rejection_is_a_failed_attempt_naming_why()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(422, BraintreeWire.ErrorResponse(
            "Gateway Rejected: cvv",
            BraintreeWire.Transaction(id: "tx_r", status: "gateway_rejected", gatewayRejectionReason: "cvv"))));

        var result = await Submit(Guid.NewGuid(), 10m, "EUR", "nonce");

        Assert.Equal(PaymentStatus.Failed, result!.Status);
        Assert.Equal("tx_r", result.ProviderReference);
        Assert.Contains("cvv", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_duplicate_rejection_is_reported_so_the_caller_can_look_the_first_charge_up()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(422, BraintreeWire.ErrorResponse(
            "Gateway Rejected: duplicate",
            BraintreeWire.Transaction(id: "tx_dup", status: "gateway_rejected", gatewayRejectionReason: "duplicate"))));

        var result = await Submit(Guid.NewGuid(), 10m, "EUR", "nonce");

        Assert.Equal(PaymentStatus.Failed, result!.Status);
        Assert.Contains("duplicate", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_rejected_nonce_creates_no_transaction_and_is_an_invalid_request()
    {
        _braintree.Respond(_ => BraintreeWire.Xml(422, BraintreeWire.ErrorResponse(
            "Unknown or expired payment_method_nonce.",
            transactionErrors: BraintreeWire.ValidationError("91565", "payment_method_nonce", "Unknown or expired payment_method_nonce."))));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Submit(Guid.NewGuid(), 10m, "EUR", "fake-consumed-nonce"));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Contains("payment_method_nonce", exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.001")]
    public async Task Refuses_an_amount_Braintree_cannot_charge_without_calling_it(string amount)
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() =>
            Submit(Guid.NewGuid(), decimal.Parse(amount, CultureInfo.InvariantCulture), "EUR", "nonce"));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_braintree.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refuses_a_missing_nonce_without_calling_Braintree(string? nonce)
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Submit(Guid.NewGuid(), 10m, "EUR", nonce!));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_braintree.Requests);
    }

    [Fact]
    public async Task Refuses_an_empty_order_id_because_it_is_what_a_lost_sale_is_found_by()
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Submit(Guid.Empty, 10m, "EUR", "nonce"));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_braintree.Requests);
    }

    [Fact]
    public async Task Refuses_a_currency_it_has_no_merchant_account_for_without_charging()
    {
        var exception = await Assert.ThrowsAsync<PaymentProviderException>(() => Submit(Guid.NewGuid(), 10m, "GBP", "nonce"));

        Assert.Equal(PaymentProviderErrorKind.InvalidRequest, exception.Kind);
        Assert.Empty(_braintree.Requests);
    }

    private Task<PaymentResult?> Submit(Guid orderId, decimal amount, string currency, string nonce) =>
        Gateways.Braintree(_braintree).SubmitPaymentMethodAsync(new SubmitPaymentMethodRequest(orderId, amount, currency, nonce));
}
