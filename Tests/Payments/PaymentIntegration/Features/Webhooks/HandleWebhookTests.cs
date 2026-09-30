using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentIntegration.Fixtures;
using PaymentProvider.Models;
using PaymentSystem.Domain.Events;
using PaymentSystem.Enums;
using PaymentSystem.Features.Webhooks;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.Webhooks;

public sealed class HandleWebhookTests(PaymentsFixture fixture) : PspTest(fixture)
{
    private static Dictionary<string, string> Route(string provider) => new() { ["provider"] = provider };

    // --- Every status the provider can report ---

    [Theory]
    [InlineData(PaymentStatus.Succeeded, PaymentOrderStatus.Success, "applied")]
    [InlineData(PaymentStatus.Canceled, PaymentOrderStatus.Failed, "applied")]
    // A failed attempt is not final: the customer can still retry with another payment method.
    [InlineData(PaymentStatus.Failed, PaymentOrderStatus.Executing, "unchanged")]
    [InlineData(PaymentStatus.AwaitingPaymentMethod, PaymentOrderStatus.Executing, "unchanged")]
    [InlineData(PaymentStatus.RequiresAction, PaymentOrderStatus.Executing, "unchanged")]
    [InlineData(PaymentStatus.Processing, PaymentOrderStatus.Executing, "unchanged")]
    [InlineData(PaymentStatus.Authorized, PaymentOrderStatus.Executing, "unchanged")]
    public async Task Maps_each_provider_status_onto_the_order(PaymentStatus reported, PaymentOrderStatus expected,
        string outcome)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await WebhookAsync(checkout.OrderId(0), reported);

        Assert.True(result.IsSuccess);
        Assert.Equal(outcome, result.Value!.Outcome);
        Assert.Equal(expected, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public void Maps_every_status_the_provider_defines()
    {
        // Guards the theory above: a status added to PaymentStatus must get a decision, not fall through.
        var covered = new[]
        {
            PaymentStatus.Succeeded, PaymentStatus.Canceled, PaymentStatus.Failed, PaymentStatus.AwaitingPaymentMethod,
            PaymentStatus.RequiresAction, PaymentStatus.Processing, PaymentStatus.Authorized
        };

        Assert.Equal(Enum.GetValues<PaymentStatus>().Order(), covered.Order());
    }

    [Fact]
    public async Task A_success_settles_the_checkout_once_every_order_has_succeeded()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success, OrderState.Executing);

        await WebhookAsync(checkout.OrderId(1), PaymentStatus.Succeeded);

        Assert.True((await ReadCheckoutAsync(checkout.CheckoutId)).IsPaymentDone);
        var published = Assert.Single(Events.Published);
        Assert.Equal(new PaymentOrderSucceededDomainEvent(checkout.OrderId(1), checkout.CheckoutId), published.Event);
    }

    // --- At least once, out of order ---

    [Fact]
    public async Task A_duplicate_success_is_a_no_op_and_raises_nothing_twice()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);

        var first = await WebhookAsync(id, PaymentStatus.Succeeded, "evt_1");
        var again = await WebhookAsync(id, PaymentStatus.Succeeded, "evt_1");

        Assert.Equal("applied", first.Value!.Outcome);
        Assert.Equal("unchanged", again.Value!.Outcome);
        Assert.Single(Events.Published);
        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(id)).Status);
    }

    [Fact]
    public async Task A_duplicate_cancellation_is_a_no_op()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);

        await WebhookAsync(id, PaymentStatus.Canceled, "evt_1");
        var again = await WebhookAsync(id, PaymentStatus.Canceled, "evt_1");

        Assert.Equal("unchanged", again.Value!.Outcome);
        Assert.Single(Events.Published);
    }

    [Theory]
    [InlineData(PaymentStatus.Canceled)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.RequiresAction)]
    [InlineData(PaymentStatus.AwaitingPaymentMethod)]
    [InlineData(PaymentStatus.Authorized)]
    public async Task An_older_event_arriving_after_success_never_overwrites_it(PaymentStatus older)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);
        await WebhookAsync(id, PaymentStatus.Succeeded);
        Events.Reset();

        var result = await WebhookAsync(id, older);

        // 200, not an error: anything else and the provider keeps redelivering a stale event.
        Assert.True(result.IsSuccess);
        Assert.Equal("unchanged", result.Value!.Outcome);
        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Success, stored.Order(id).Status);
        Assert.True(stored.IsPaymentDone);
        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task A_success_arriving_after_cancellation_is_acknowledged_kept_failed_and_flagged()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);
        await WebhookAsync(id, PaymentStatus.Canceled);
        Events.Reset();

        var result = await WebhookAsync(id, PaymentStatus.Succeeded);

        Assert.Equal("unchanged", result.Value!.Outcome);
        Assert.Equal(PaymentOrderStatus.Failed, (await ReadOrderAsync(id)).Status);
        Assert.Empty(Events.Published);
        // Money may have moved on a failed order: that is somebody's job, so it is logged, not swallowed.
        Assert.Contains(Logs.Lines, line => line.StartsWith("Warning") && line.Contains("needs reconciling"));
    }

    [Fact]
    public async Task A_failed_attempt_then_a_success_still_settles()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);

        await WebhookAsync(id, PaymentStatus.Failed);
        var result = await WebhookAsync(id, PaymentStatus.Succeeded);

        Assert.Equal("applied", result.Value!.Outcome);
        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(id)).Status);
    }

    [Fact]
    public async Task A_final_status_for_an_order_not_started_yet_is_a_conflict_so_the_provider_retries()
    {
        var checkout = await SeedCheckoutAsync(OrderState.NotStarted);

        var result = await WebhookAsync(checkout.OrderId(0), PaymentStatus.Succeeded);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal(PaymentOrderStatus.NotStarted, (await OrderAsync(checkout, 0)).Status);
    }

    // --- What is not ours ---

    [Fact]
    public async Task An_unknown_order_is_acknowledged_and_ignored()
    {
        var result = await WebhookAsync(Guid.NewGuid(), PaymentStatus.Succeeded);

        Assert.Equal("ignored", result.Value!.Outcome);
    }

    [Fact]
    public async Task An_event_that_names_no_order_is_ignored()
    {
        var result = await SendAsync(new HandleWebhook.Command("stripe", WebhookBody(null, PaymentStatus.Succeeded), Signed));

        Assert.Equal("ignored", result.Value!.Outcome);
    }

    [Fact]
    public async Task A_verified_event_that_is_not_about_a_pay_in_is_ignored()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await SendAsync(new HandleWebhook.Command("stripe", WebhookBody(checkout.OrderId(0), null), Signed));

        Assert.Equal("ignored", result.Value!.Outcome);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
    }

    [Theory]
    [InlineData("paypal")]
    [InlineData("1")]
    [InlineData("")]
    public async Task An_unknown_provider_is_not_found(string provider)
    {
        var result = await SendAsync(new HandleWebhook.Command(provider, WebhookBody(Guid.NewGuid(), PaymentStatus.Succeeded), Signed));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task The_provider_is_matched_regardless_of_case()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var result = await WebhookAsync(checkout.OrderId(0), PaymentStatus.Succeeded, provider: "BrainTree");

        Assert.Equal("applied", result.Value!.Outcome);
    }

    // --- Authenticity ---

    [Theory]
    [InlineData("forged")]
    [InlineData(null)]
    public async Task An_unverifiable_webhook_is_400_and_changes_nothing(string? signature)
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var headers = signature is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [StubGateway.SignatureHeader] = signature };

        var response = await Endpoint(new HandleWebhookEndpoints()).PostAsync(Route("stripe"),
            WebhookBody(checkout.OrderId(0), PaymentStatus.Succeeded), headers);

        Assert.Equal(400, response.Status);
        Assert.Contains("invalid_webhook", response.Body);
        Assert.Equal(PaymentOrderStatus.Executing, (await OrderAsync(checkout, 0)).Status);
        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task The_endpoint_needs_no_caller_identity_and_hands_the_raw_body_and_headers_to_the_provider()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);

        var response = await Endpoint(new HandleWebhookEndpoints()).PostAsync(Route("stripe"),
            WebhookBody(checkout.OrderId(0), PaymentStatus.Succeeded),
            new Dictionary<string, string> { [StubGateway.SignatureHeader.ToLowerInvariant()] = StubGateway.ValidSignature });

        Assert.Equal(200, response.Status);
        Assert.Contains("applied", response.Body);
        Assert.Equal(PaymentOrderStatus.Success, (await OrderAsync(checkout, 0)).Status);
    }

    [Fact]
    public async Task The_signature_is_never_logged()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        const string forged = "t=1,v1=forged-signature-value";

        await Endpoint(new HandleWebhookEndpoints()).PostAsync(Route("stripe"),
            WebhookBody(checkout.OrderId(0), PaymentStatus.Succeeded),
            new Dictionary<string, string> { [StubGateway.SignatureHeader] = forged });

        Assert.Contains(Logs.Lines, line => line.Contains("Rejected"));
        Assert.DoesNotContain(Logs.Lines, line => line.Contains(forged));
    }

    // --- Concurrent callbacks ---

    [Fact]
    public async Task A_sibling_settled_concurrently_is_seen_on_retry_and_the_checkout_ends_done()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);

        await using (var other = await SettleUncommittedAsync(checkout.CheckoutId, checkout.OrderId(1)))
        {
            var webhook = WebhookAsync(checkout.OrderId(0), PaymentStatus.Succeeded);
            await WaitUntilBlockedAsync();
            await other.CommitAsync();

            var result = await webhook;
            Assert.Equal("applied", result.Value!.Outcome);
        }

        var stored = await ReadCheckoutAsync(checkout.CheckoutId);
        Assert.All(stored.PaymentOrders, o => Assert.Equal(PaymentOrderStatus.Success, o.Status));
        Assert.True(stored.IsPaymentDone);
    }

    [Fact]
    public async Task The_same_order_settled_concurrently_is_a_no_op_on_retry()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);

        await using (var other = await SettleUncommittedAsync(checkout.CheckoutId, id))
        {
            var webhook = WebhookAsync(id, PaymentStatus.Succeeded);
            await WaitUntilBlockedAsync();
            await other.CommitAsync();

            var result = await webhook;
            Assert.Equal("unchanged", result.Value!.Outcome);
        }

        Assert.Empty(Events.Published);
        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(id)).Status);
    }

    [Fact]
    public async Task A_conflict_inside_settlement_is_not_retried_as_a_redelivery_and_commits_nothing()
    {
        // The dispatch runs after this write landed, so a retry would find the order settled, call it a
        // duplicate and commit it without its settlement. It must roll back and let the provider redeliver.
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var id = checkout.OrderId(0);
        Events.OnPublish = (_, _) => throw new DbUpdateConcurrencyException("A settlement handler lost a race.");

        var result = await WebhookAsync(id, PaymentStatus.Succeeded);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Single(Events.Published);
        Assert.Equal(PaymentOrderStatus.Executing, (await ReadOrderAsync(id)).Status);
    }

    // Another callback's write, committed only when the test says: it settles one order and bumps the root's
    // version the way AggregateVersionInterceptor does, holding the row locks until then.
    private async Task<UncommittedWrite> SettleUncommittedAsync(Guid checkoutId, Guid paymentOrderId)
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE "PaymentOrders" SET "Status" = 'Success' WHERE "PaymentOrderId" = @order;
            UPDATE "PaymentEvents" SET "Version" = "Version" + 1 WHERE "CheckoutId" = @checkout;
            """;
        command.Parameters.AddWithValue("order", paymentOrderId);
        command.Parameters.AddWithValue("checkout", checkoutId);
        await command.ExecuteNonQueryAsync();
        return new UncommittedWrite(connection, transaction);
    }

    private sealed class UncommittedWrite(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public Task CommitAsync() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    // The handler has read the old version and is now waiting on the row lock to write — the exact window a
    // concurrent callback would hit.
    private async Task WaitUntilBlockedAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()",
                connection);
            if ((long)(await command.ExecuteScalarAsync())! > 0)
                return;
            await Task.Delay(20);
        }

        throw new TimeoutException("The webhook never blocked on the concurrent write.");
    }
}
