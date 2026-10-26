using PaymentAdapters.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

// What Stripe settled is its balance transactions. Each line must name its money the way our books do — a charge by
// its payment intent, a refund by the refund's id — in the source's own amount and currency.
public sealed class StripeSettlementTests : IDisposable
{
    private static readonly DateTime From = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeHttpServer _stripe = new();

    public void Dispose() => _stripe.Dispose();

    private Task<IReadOnlyList<SettlementLine>> Settle() =>
        Gateways.Stripe(_stripe).GetSettlementAsync(new SettlementRequest(From, To));

    [Fact]
    public async Task Lists_the_window_with_each_source_read()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.BalanceList()));

        await Settle();

        var request = Assert.Single(_stripe.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/v1/balance_transactions", request.Path);
        Assert.Equal(new DateTimeOffset(From).ToUnixTimeSeconds().ToString(), request.QueryString["created[gte]"]);
        Assert.Equal(new DateTimeOffset(To).ToUnixTimeSeconds().ToString(), request.QueryString["created[lt]"]);
        Assert.Equal("data.source", request.QueryString["expand[0]"]);
    }

    [Fact]
    public async Task A_charge_is_named_by_its_payment_intent_in_the_charges_own_amount_and_currency()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.BalanceList(
            StripeWire.BalanceTransaction("txn_1", "charge", StripeWire.ChargeOf("pi_9", 1099, "usd")))));

        var line = Assert.Single(await Settle());

        Assert.Equal(new SettlementLine("pi_9", SettlementLineKind.Charge, 10.99m, "USD"), line);
    }

    [Fact]
    public async Task A_zero_decimal_currency_is_not_divided()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.BalanceList(
            StripeWire.BalanceTransaction("txn_1", "charge", StripeWire.ChargeOf("pi_9", 500, "jpy")))));

        Assert.Equal(500m, Assert.Single(await Settle()).Amount);
    }

    [Fact]
    public async Task A_refund_is_named_by_its_id_with_the_ids_it_was_tagged_with()
    {
        var orderId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.BalanceList(
            StripeWire.BalanceTransaction("txn_2", "refund",
                StripeWire.Refund(id: "re_7", status: "pending", refundId: refundId, orderId: orderId)))));

        var line = Assert.Single(await Settle());

        Assert.Equal(new SettlementLine("re_7", SettlementLineKind.Refund, 10.99m, "USD", orderId, refundId), line);
    }

    [Fact]
    public async Task A_refund_failure_is_named_by_the_refund_it_took_back()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.BalanceList(
            StripeWire.BalanceTransaction("txn_3", "refund_failure", StripeWire.Refund(id: "re_7", status: "failed")))));

        var line = Assert.Single(await Settle());

        Assert.Equal(("re_7", SettlementLineKind.RefundFailure), (line.ProviderReference, line.Kind));
    }

    [Fact]
    public async Task Fees_payouts_and_other_movements_are_left_out()
    {
        _stripe.Respond(_ => StripeWire.Ok(StripeWire.BalanceList(
            StripeWire.BalanceTransaction("txn_4", "payout", StripeWire.Payout()),
            StripeWire.BalanceTransaction("txn_5", "charge", StripeWire.ChargeOf("pi_1", 100, "usd")))));

        Assert.Equal("pi_1", Assert.Single(await Settle()).ProviderReference);
    }

    [Fact]
    public async Task Stripe_being_down_is_transient()
    {
        _stripe.Respond(_ => StripeWire.Error(503, "api_error"));

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(Settle);

        Assert.Equal(PaymentProviderErrorKind.Transient, exception.Kind);
    }
}
