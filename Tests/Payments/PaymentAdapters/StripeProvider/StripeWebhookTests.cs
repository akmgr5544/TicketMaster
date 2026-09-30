using PaymentAdapters.Fixtures;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentAdapters.StripeProvider;

// The webhook endpoint is public. Anything that is not provably from Stripe must be refused, and
// nothing about the event may be read before the signature is checked.
public sealed class StripeWebhookTests
{
    private readonly IPaymentGateway _gateway = Gateways.Stripe(new FakeHttpServer());

    [Fact]
    public void A_signed_success_names_the_order_and_the_outcome()
    {
        var orderId = Guid.NewGuid();
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(id: "pi_9", status: "succeeded", orderId: orderId), id: "evt_42");

        var webhook = Parse(body, Sign(body));

        Assert.NotNull(webhook);
        Assert.Equal("evt_42", webhook.EventId);
        Assert.Equal(orderId, webhook.PaymentOrderId);
        Assert.Equal(new PaymentResult("pi_9", PaymentStatus.Succeeded), webhook.Payment);
    }

    [Fact]
    public void A_signed_decline_is_a_failed_attempt_with_the_reason()
    {
        var body = StripeWire.Event("payment_intent.payment_failed",
            StripeWire.Intent(status: "requires_payment_method", orderId: Guid.NewGuid(), lastError: "Insufficient funds."));

        var webhook = Parse(body, Sign(body));

        Assert.Equal(PaymentStatus.Failed, webhook!.Payment.Status);
        Assert.Equal("Insufficient funds.", webhook.Payment.FailureReason);
    }

    [Fact]
    public void Parsing_the_same_delivery_twice_gives_the_same_event_id_to_dedupe_on()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"), id: "evt_same");

        Assert.Equal(Parse(body, Sign(body))!.EventId, Parse(body, Sign(body))!.EventId);
    }

    [Fact]
    public void The_signature_header_is_found_whatever_its_casing()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));

        var webhook = _gateway.ParseWebhook(new WebhookRequest(body, new Dictionary<string, string> { ["stripe-signature"] = Sign(body) }));

        Assert.NotNull(webhook);
    }

    [Fact]
    public void A_missing_signature_is_refused()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));

        AssertRefused(() => _gateway.ParseWebhook(new WebhookRequest(body, new Dictionary<string, string>())));
    }

    [Fact]
    public void A_body_changed_after_signing_is_refused()
    {
        var body = StripeWire.Event("payment_intent.payment_failed", StripeWire.Intent(status: "requires_payment_method", lastError: "declined"));
        var forged = body.Replace("payment_intent.payment_failed", "payment_intent.succeeded").Replace("requires_payment_method", "succeeded");

        AssertRefused(() => Parse(forged, Sign(body)));
    }

    [Fact]
    public void A_signature_made_with_another_secret_is_refused()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));

        AssertRefused(() => Parse(body, StripeWire.Sign(body, "whsec_attacker", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void A_replay_of_an_old_genuine_delivery_is_refused()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));

        AssertRefused(() => Parse(body, StripeWire.Sign(body, Gateways.StripeWebhookSecret, DateTimeOffset.UtcNow.AddMinutes(-10))));
    }

    [Fact]
    public void A_signature_dated_far_in_the_future_is_refused()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));

        AssertRefused(() => Parse(body, StripeWire.Sign(body, Gateways.StripeWebhookSecret, DateTimeOffset.UtcNow.AddDays(1))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("t=,v1=")]
    [InlineData("v1=abc")]
    [InlineData("t=1700000000")]
    public void A_malformed_signature_header_is_refused(string header)
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));

        AssertRefused(() => Parse(body, header));
    }

    [Fact]
    public void A_signature_under_a_scheme_other_than_v1_is_ignored()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));
        var v0Only = Sign(body).Replace("v1=", "v0=");

        AssertRefused(() => Parse(body, v0Only));
    }

    [Fact]
    public void During_a_secret_roll_one_valid_signature_among_several_is_enough()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"));
        var genuine = Sign(body);
        var header = genuine + ",v1=" + new string('0', 64);

        Assert.NotNull(Parse(body, header));
    }

    [Fact]
    public void An_event_that_is_not_about_a_payment_intent_is_verified_then_ignored()
    {
        var body = StripeWire.Event("charge.refunded", StripeWire.Charge());

        Assert.Null(Parse(body, Sign(body)));
    }

    [Fact]
    public void An_unrelated_event_is_still_refused_when_the_signature_is_wrong()
    {
        var body = StripeWire.Event("charge.refunded", StripeWire.Charge());

        AssertRefused(() => Parse(body, StripeWire.Sign(body, "whsec_attacker", DateTimeOffset.UtcNow)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void An_intent_whose_order_tag_is_missing_or_garbled_still_reports_its_payment(string? rawOrderId)
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded", rawOrderId: rawOrderId));

        var webhook = Parse(body, Sign(body));

        Assert.NotNull(webhook);
        Assert.Null(webhook.PaymentOrderId);
        Assert.Equal(PaymentStatus.Succeeded, webhook.Payment.Status);
    }

    // An API version mismatch is our endpoint being configured against the wrong version, not an
    // attacker. Calling it a bad signature sends the on-call looking for the wrong problem.
    [Fact]
    public void An_event_from_another_API_version_is_a_configuration_error_not_a_forgery()
    {
        var body = StripeWire.Event("payment_intent.succeeded", StripeWire.Intent(status: "succeeded"), apiVersion: "2019-02-19");

        var exception = Assert.Throws<PaymentProviderException>(() => Parse(body, Sign(body)));

        Assert.Equal(PaymentProviderErrorKind.Configuration, exception.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void A_genuinely_signed_body_that_is_not_an_event_is_a_provider_error_not_a_crash(string body)
    {
        Assert.Throws<PaymentProviderException>(() => Parse(body, Sign(body)));
    }

    private WebhookEvent? Parse(string body, string signature) =>
        _gateway.ParseWebhook(new WebhookRequest(body, new Dictionary<string, string> { ["Stripe-Signature"] = signature }));

    private static string Sign(string body) => StripeWire.Sign(body, Gateways.StripeWebhookSecret, DateTimeOffset.UtcNow);

    private static void AssertRefused(Func<WebhookEvent?> parse)
    {
        var exception = Assert.Throws<PaymentProviderException>(parse);
        Assert.Equal(PaymentProviderErrorKind.InvalidSignature, exception.Kind);
    }
}
