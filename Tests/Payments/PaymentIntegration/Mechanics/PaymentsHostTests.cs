using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;

namespace PaymentIntegration.Mechanics;

// What only the running host can show: messages crossing RabbitMQ, Wolverine's transaction and outbox around a
// handler, durable storage of the scheduled expiry, and the webhook verified by the real Stripe adapter. The fast
// suite proves each handler's decisions; this one proves they are wired into a host that keeps its promises.
[Collection(PaymentsHostCollection.Name)]
public sealed class PaymentsHostTests(PaymentsHostFixture fixture)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    [Fact]
    public void Every_application_endpoint_is_durable()
    {
        var endpoints = fixture.Services.GetRequiredService<IWolverineRuntime>().Options.Transports
            .AllEndpoints()
            .Where(endpoint => endpoint.Role == EndpointRole.Application)
            // Wolverine's own sink for messages that already failed; it carries no message of this service's.
            .Where(endpoint => !endpoint.Uri.ToString().Contains("wolverine-dead-letter-queue"))
            .ToArray();

        // Without durability a crash loses a PaymentRequested mid-handler, a BookingPaid is sent from memory, and a
        // checkout's expiry is forgotten on restart — each of which leaves seats held forever.
        Assert.NotEmpty(endpoints);
        Assert.Empty(endpoints.Where(endpoint => endpoint.Mode != EndpointMode.Durable)
            .Select(endpoint => $"{endpoint.Uri} listener={endpoint.IsListener}"));
    }

    [Fact]
    public async Task A_payment_request_over_the_broker_opens_a_checkout_and_stores_its_expiry()
    {
        var bookingId = NewBookingId();

        await fixture.Bookings.PublishAsync(new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), 75m, "USD"));

        await Eventually(async () => await CheckoutExistsAsync(bookingId));
        // Stored, not held in memory: a restart within the fifteen minutes must not lose it.
        Assert.Equal(1, await ScheduledExpiriesAsync(bookingId));
    }

    [Fact]
    public async Task A_signed_webhook_success_settles_the_order_and_Bookings_hears_it_was_paid()
    {
        var checkout = await SeedExecutingCheckoutAsync();
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        using var response = await SendStripeWebhookAsync(orderId, "succeeded");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await fixture.Outcomes.WaitForAsync<BookingPaidIntegrationEvent>(paid => paid.BookingId == checkout.BookingId, Wait);
        Assert.Equal(100m, await ScalarAsync<decimal>(
            "select \"CurrentBalance\" from \"Wallets\" where \"UserId\" = @id", ("id", checkout.PaymentOrders.Single().SellerId)));
    }

    [Fact]
    public async Task A_webhook_with_a_forged_signature_is_refused_and_changes_nothing()
    {
        var checkout = await SeedExecutingCheckoutAsync();
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        using var response = await SendStripeWebhookAsync(orderId, "succeeded", secret: "whsec_not_the_real_one");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Executing", await OrderStatusAsync(orderId));
    }

    [Theory]
    [InlineData("paypal")]      // no such provider
    [InlineData("braintree")]   // a real provider, but not configured on this host
    public async Task A_webhook_for_a_provider_this_host_does_not_run_is_not_found(string provider)
    {
        using var client = fixture.CreateClient();

        using var response = await client.PostAsync($"api/payments/webhooks/{provider}", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_webhook_with_a_status_that_is_not_final_is_acknowledged_and_changes_nothing()
    {
        var checkout = await SeedExecutingCheckoutAsync();
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        using var response = await SendStripeWebhookAsync(orderId, "processing");

        // Acknowledged, or the PSP would keep resending an event that is not meant to change anything.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Executing", await OrderStatusAsync(orderId));
    }

    [Fact]
    public async Task A_signed_refund_success_finishes_the_refund_and_Bookings_hears_it_was_refunded()
    {
        var refundId = Guid.NewGuid();
        var (checkout, refund) = await SeedProcessingRefundAsync(refundId);

        using var response = await SendStripeRefundWebhookAsync(refund.PaymentOrderId, refundId, $"re_{refundId:N}", "succeeded");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await fixture.Outcomes.WaitForAsync<BookingRefundedIntegrationEvent>(
            refunded => refunded.BookingId == checkout.BookingId && refunded.RefundId == refundId, Wait);
        Assert.Equal("Succeeded", await RefundStatusAsync(refund.RefundOrderId));
        Assert.Equal("Refunded", await OrderStatusAsync(refund.PaymentOrderId));
    }

    [Fact]
    public async Task A_signed_refund_failure_fails_the_refund_and_leaves_the_payment_where_it_was()
    {
        var refundId = Guid.NewGuid();
        var (_, refund) = await SeedProcessingRefundAsync(refundId);

        using var response = await SendStripeRefundWebhookAsync(
            refund.PaymentOrderId, refundId, $"re_{refundId:N}", "failed", "expired_or_canceled_card");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Failed", await RefundStatusAsync(refund.RefundOrderId));
        Assert.Equal("Success", await OrderStatusAsync(refund.PaymentOrderId));
    }

    // Stripe sends no request id for a refund made outside Payments' flow; the reference alone still finds ours.
    [Fact]
    public async Task A_refund_event_without_our_ids_is_matched_by_its_reference()
    {
        var refundId = Guid.NewGuid();
        var (_, refund) = await SeedProcessingRefundAsync(refundId);

        using var response = await SendStripeRefundWebhookAsync(refund.PaymentOrderId, null, $"re_{refundId:N}", "failed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Failed", await RefundStatusAsync(refund.RefundOrderId));
    }

    [Fact]
    public async Task A_refund_still_pending_or_unknown_is_acknowledged_and_changes_nothing()
    {
        var refundId = Guid.NewGuid();
        var (_, refund) = await SeedProcessingRefundAsync(refundId);

        using var pending = await SendStripeRefundWebhookAsync(refund.PaymentOrderId, refundId, $"re_{refundId:N}", "pending");
        using var unknown = await SendStripeRefundWebhookAsync(Guid.NewGuid(), Guid.NewGuid(), "re_nobody_asked_for", "succeeded");

        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal("Processing", await RefundStatusAsync(refund.RefundOrderId));
    }

    [Fact]
    public async Task A_cancelled_booking_over_the_broker_fails_its_payment_and_Bookings_hears_it()
    {
        var bookingId = NewBookingId();
        await fixture.Bookings.PublishAsync(new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), 20m, "USD"));
        await Eventually(async () => await CheckoutExistsAsync(bookingId));

        await fixture.Bookings.PublishAsync(new BookingCancelledIntegrationEvent(bookingId));

        await fixture.Outcomes.WaitForAsync<BookingPaymentFailedIntegrationEvent>(failed => failed.BookingId == bookingId, Wait);
    }

    [Fact]
    public async Task A_user_registered_over_the_broker_is_kept_by_Payments()
    {
        var userId = Guid.NewGuid();

        await fixture.Bookings.PublishAsync(new UserRegisteredIntegrationEvent(userId, $"{userId:N}@example.com"));

        await Eventually(async () =>
            await ScalarAsync<long>("select count(*) from \"Users\" where \"UserId\" = @id", ("id", userId)) == 1);
    }

    // The outbox's whole promise: what a handler sends commits or rolls back with what it writes. A trigger makes
    // this one booking's insert fail inside the handler's transaction, after the expiry has been scheduled. It counts
    // each attempt in a sequence first — a sequence is not rolled back with the transaction — so the test knows when
    // the retry policy (three cooldowns, so four attempts) has been spent and nothing can still be on its way.
    [Fact]
    public async Task A_handler_whose_write_fails_leaves_no_message_behind()
    {
        var bookingId = NewBookingId();
        await ExecuteAsync($"""
            create sequence attempts_{bookingId};
            create or replace function refuse_booking_{bookingId}() returns trigger as $$
            begin
              if new."BookingId" = {bookingId} then
                perform nextval('attempts_{bookingId}');
                raise exception 'refused by test';
              end if;
              return new;
            end $$ language plpgsql;
            create trigger refuse_booking_{bookingId} before insert on "PaymentEvents"
              for each row execute function refuse_booking_{bookingId}();
            """);

        try
        {
            await fixture.Bookings.PublishAsync(new PaymentRequestedIntegrationEvent(bookingId, Guid.NewGuid(), Guid.NewGuid(), 30m, "USD"));

            await Eventually(async () => await AttemptsAsync(bookingId) >= 4, TimeSpan.FromSeconds(40));
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.False(await CheckoutExistsAsync(bookingId));
            Assert.Equal(0, await ScheduledExpiriesAsync(bookingId));
            Assert.Empty(fixture.Outcomes.For<BookingPaymentFailedIntegrationEvent>(failed => failed.BookingId == bookingId));
        }
        finally
        {
            await ExecuteAsync($"""
                drop trigger refuse_booking_{bookingId} on "PaymentEvents";
                drop function refuse_booking_{bookingId}();
                drop sequence attempts_{bookingId};
                """);
        }
    }

    private static long NewBookingId() => Random.Shared.NextInt64(1, int.MaxValue);

    // What StartCheckout leaves behind once Stripe has answered; Stripe itself is not reachable from a test.
    private async Task<PaymentEvent> SeedExecutingCheckoutAsync()
    {
        var checkout = PaymentEvent.Create(NewBookingId(), Guid.NewGuid(), "USD");
        checkout.AddPaymentOrder(Guid.NewGuid(), 100m);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        checkout.StartOrder(orderId, "Stripe", $"pi_{orderId:N}", DateTime.UtcNow);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        context.PaymentEvents.Add(checkout);
        await context.SaveChangesAsync();
        return checkout;
    }

    // Signed the way Stripe signs: v1 = HMAC-SHA256(secret, "{timestamp}.{body}"), over the exact bytes sent.
    private async Task<HttpResponseMessage> SendStripeWebhookAsync(Guid orderId, string status,
        string secret = PaymentsHostFixture.StripeWebhookSecret)
    {
        var eventType = status == "succeeded" ? "payment_intent.succeeded" : "payment_intent.processing";
        var body = $$"""
            {
              "id": "evt_{{Guid.NewGuid():N}}",
              "object": "event",
              "api_version": "{{Stripe.StripeConfiguration.ApiVersion}}",
              "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
              "type": "{{eventType}}",
              "livemode": false,
              "pending_webhooks": 1,
              "data": {
                "object": {
                  "id": "pi_{{orderId:N}}",
                  "object": "payment_intent",
                  "amount": 10000,
                  "currency": "usd",
                  "status": "{{status}}",
                  "client_secret": "pi_{{orderId:N}}_secret",
                  "metadata": { "payment_order_id": "{{orderId}}" }
                }
              }
            }
            """;

        return await SendSignedStripeAsync(body, secret);
    }

    private async Task<HttpResponseMessage> SendStripeRefundWebhookAsync(Guid orderId, Guid? refundId, string reference,
        string status, string? failureReason = null)
    {
        var metadata = refundId is { } id
            ? $$"""{ "payment_order_id": "{{orderId}}", "refund_id": "{{id}}" }"""
            : "{}";
        var body = $$"""
            {
              "id": "evt_{{Guid.NewGuid():N}}",
              "object": "event",
              "api_version": "{{Stripe.StripeConfiguration.ApiVersion}}",
              "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
              "type": "refund.updated",
              "livemode": false,
              "pending_webhooks": 1,
              "data": {
                "object": {
                  "id": "{{reference}}",
                  "object": "refund",
                  "amount": 10000,
                  "currency": "usd",
                  "payment_intent": "pi_{{orderId:N}}",
                  "status": "{{status}}",
                  "failure_reason": {{(failureReason is null ? "null" : $"\"{failureReason}\"")}},
                  "metadata": {{metadata}}
                }
              }
            }
            """;

        return await SendSignedStripeAsync(body, PaymentsHostFixture.StripeWebhookSecret);
    }

    private async Task<HttpResponseMessage> SendSignedStripeAsync(string body, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));

        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/payments/webhooks/stripe")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={Convert.ToHexStringLower(signature)}");
        return await client.SendAsync(request);
    }

    private async Task<bool> CheckoutExistsAsync(long bookingId) =>
        await ScalarAsync<long>("select count(*) from \"PaymentEvents\" where \"BookingId\" = @id", ("id", bookingId)) > 0;

    // A paid order whose refund Stripe accepted without finishing, with the seller's wallet holding the payment.
    private async Task<(PaymentEvent Checkout, RefundOrder Refund)> SeedProcessingRefundAsync(Guid refundId)
    {
        var checkout = PaymentEvent.Create(NewBookingId(), Guid.NewGuid(), "USD");
        checkout.AddPaymentOrder(Guid.NewGuid(), 100m);
        var order = checkout.PaymentOrders.Single();
        checkout.StartOrder(order.PaymentOrderId, "Stripe", $"pi_{order.PaymentOrderId:N}", DateTime.UtcNow);
        checkout.SucceedOrder(order.PaymentOrderId, $"pi_{order.PaymentOrderId:N}");
        checkout.MarkOrderSettled(order.PaymentOrderId);
        var wallet = new Wallet(order.SellerId, "USD");
        wallet.CreditFunds(order.Amount);
        var refund = RefundOrder.Create(order.PaymentOrderId, refundId, order.Amount, "Booking cancelled");
        refund.MarkAsProcessing($"re_{refundId:N}", DateTime.UtcNow);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        context.PaymentEvents.Add(checkout);
        context.Wallets.Add(wallet);
        context.RefundOrders.Add(refund);
        await context.SaveChangesAsync();
        return (checkout, refund);
    }

    private Task<string> RefundStatusAsync(Guid refundOrderId) =>
        ScalarAsync<string>("select \"Status\" from \"RefundOrders\" where \"RefundOrderId\" = @id", ("id", refundOrderId));

    private Task<string> OrderStatusAsync(Guid orderId) =>
        ScalarAsync<string>("select \"Status\" from \"PaymentOrders\" where \"PaymentOrderId\" = @id", ("id", orderId));

    private async Task<int> ScheduledExpiriesAsync(long bookingId) =>
        (int)await ScalarAsync<long>(
            "select count(*) from wolverine.wolverine_incoming_envelopes where message_type = 'checkout-expiry-due' "
            + "and status = 'Scheduled' and position(@booking in body) > 0",
            ("booking", Encoding.UTF8.GetBytes(bookingId.ToString())));

    private Task<long> AttemptsAsync(long bookingId) =>
        ScalarAsync<long>($"select case when is_called then last_value else 0 end from attempts_{bookingId}");

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task Eventually(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Wait);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(200);
        }

        throw new TimeoutException("The condition did not hold in time.");
    }
}
