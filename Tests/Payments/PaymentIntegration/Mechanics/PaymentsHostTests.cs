using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Runtime.Routing;

namespace PaymentIntegration.Mechanics;

[Collection(PaymentsHostCollection.Name)]
public sealed class PaymentsHostTests(PaymentsHostFixture fixture)
{
    // Generous: the durable listener and sender both go through Postgres before and after the broker.
    private static readonly TimeSpan Relay = TimeSpan.FromSeconds(60);

    // Long enough for a message that was going to be sent to arrive; nothing in the relay is slower.
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(5);

    [Fact]
    public void The_host_starts()
    {
        Assert.NotNull(fixture.Services.GetRequiredService<IWolverineRuntime>());
    }

    [Fact]
    public void Each_consumed_contract_has_one_durable_broker_listener()
    {
        var listeners = BrokerEndpoints().Where(endpoint => endpoint.IsListener).ToArray();

        Assert.Equal(3, listeners.Length);
        Assert.Single(listeners, l => l.Uri.ToString().Contains(nameof(PaymentRequestedIntegrationEvent)));
        Assert.Single(listeners, l => l.Uri.ToString().Contains(nameof(BookingCancelledIntegrationEvent)));
        Assert.Single(listeners, l => l.Uri.ToString().Contains(nameof(RefundRequestedIntegrationEvent)));
        Assert.All(listeners, listener => Assert.Equal(EndpointMode.Durable, listener.Mode));
    }

    // Both are this service's own business: on the broker they would be published to every consumer of the type.
    [Theory]
    [InlineData(typeof(CheckoutExpiryDue))]
    [InlineData(typeof(CheckoutRefundDue))]
    public void Local_messages_stay_on_a_durable_local_queue(Type message)
    {
        var routes = fixture.Services.GetRequiredService<IWolverineRuntime>().RoutingFor(message).Routes
            .OfType<MessageRoute>()
            .ToArray();

        var route = Assert.Single(routes);
        Assert.Equal("local", route.Uri.Scheme);
        Assert.True(route.Sender.IsDurable, $"{route.Uri} is not durable.");
    }

    [Fact]
    public async Task A_new_checkout_stores_its_expiry_timer_due_fifteen_minutes_later()
    {
        var request = new PaymentRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid(), Guid.NewGuid(), 12m, "USD");
        var before = DateTimeOffset.UtcNow;

        await fixture.Bookings.PublishAsync(request);
        var checkout = await WaitForCheckoutAsync(request.BookingId);

        var due = Assert.Single(await ScheduledExpiriesAsync(checkout.CheckoutId));
        Assert.InRange(due, before + ExpireCheckout.After - TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow + ExpireCheckout.After);
    }

    [Fact]
    public async Task A_rolled_back_checkout_takes_its_expiry_timer_with_it()
    {
        var bookingId = NewBookingId();

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new RequestPayment.Command(bookingId, Guid.NewGuid(), Guid.NewGuid(), 3m, "USD"));
            Assert.True(result.IsSuccess);
            Assert.Single(await ScheduledExpiriesAsync(result.Value!.CheckoutId, context));

            await transaction.RollbackAsync();

            Assert.Empty(await ScheduledExpiriesAsync(result.Value.CheckoutId));
        }

        Assert.Equal(0, await ReadAsync(c => c.PaymentEvents.CountAsync(e => e.BookingId == bookingId)));
    }

    [Fact]
    public async Task An_expiry_that_comes_due_fails_the_checkout_and_relays_BookingPaymentFailed()
    {
        var checkout = await SeedExecutingCheckoutAsync();

        // Sent now rather than waiting out the window: this proves the local route and its consumer; when it
        // fires is Wolverine's scheduler, and what it does is covered in ExpireCheckoutTests.
        await using (var scope = fixture.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new CheckoutExpiryDue(checkout.CheckoutId));

        await fixture.Outcomes.WaitForAsync<BookingPaymentFailedIntegrationEvent>(
            m => m.BookingId == checkout.BookingId, Relay);
        var stored = await ReadAsync(c => c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId));
        Assert.Equal(PaymentOrderStatus.Failed, stored.Order(checkout.OrderId(0)).Status);
    }

    [Fact]
    public async Task A_BookingCancelled_off_the_broker_fails_the_checkout_and_relays_BookingPaymentFailed()
    {
        var checkout = await SeedExecutingCheckoutAsync();

        await fixture.Bookings.PublishAsync(new BookingCancelledIntegrationEvent(checkout.BookingId));

        await fixture.Outcomes.WaitForAsync<BookingPaymentFailedIntegrationEvent>(
            m => m.BookingId == checkout.BookingId, Relay);
        var stored = await ReadAsync(c => c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId));
        Assert.Equal(PaymentOrderStatus.Failed, stored.Order(checkout.OrderId(0)).Status);
    }

    [Theory]
    [InlineData(typeof(BookingPaidIntegrationEvent))]
    [InlineData(typeof(BookingPaymentFailedIntegrationEvent))]
    public void Every_outcome_is_sent_to_the_broker_durably(Type outcome)
    {
        var routes = fixture.Services.GetRequiredService<IWolverineRuntime>().RoutingFor(outcome).Routes
            .OfType<MessageRoute>()
            .ToArray();

        Assert.NotEmpty(routes);
        Assert.All(routes, route => Assert.Equal("rabbitmq", route.Uri.Scheme));
        Assert.All(routes, route => Assert.True(route.Sender.IsDurable, $"{route.Uri} is not durable."));
    }

    [Fact]
    public async Task A_PaymentRequested_off_the_broker_creates_the_checkout()
    {
        var request = new PaymentRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid(), Guid.NewGuid(), 42.10m, "USD");

        await fixture.Bookings.PublishAsync(request);

        var checkout = await WaitForCheckoutAsync(request.BookingId);
        Assert.Equal(request.BuyerId, checkout.BuyerId);
        var order = Assert.Single(checkout.PaymentOrders);
        Assert.Equal(request.SellerId, order.MerchantId);
        Assert.Equal(42.10m, order.Amount);
    }

    [Fact]
    public async Task A_redelivered_PaymentRequested_leaves_one_checkout()
    {
        var request = new PaymentRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid(), Guid.NewGuid(), 5m, "USD");

        await fixture.Bookings.PublishAsync(request);
        await WaitForCheckoutAsync(request.BookingId);
        await fixture.Bookings.PublishAsync(request);
        await Task.Delay(Silence);

        Assert.Equal(1, await ReadAsync(c => c.PaymentEvents.CountAsync(e => e.BookingId == request.BookingId)));
        Assert.Empty(fixture.Outcomes.For<BookingPaymentFailedIntegrationEvent>(m => m.BookingId == request.BookingId));
    }

    [Fact]
    public async Task A_refused_PaymentRequested_is_answered_with_BookingPaymentFailed_and_saves_nothing()
    {
        var request = new PaymentRequestedIntegrationEvent(NewBookingId(), Guid.NewGuid(), Guid.NewGuid(), 0m, "USD");

        await fixture.Bookings.PublishAsync(request);

        await fixture.Outcomes.WaitForAsync<BookingPaymentFailedIntegrationEvent>(
            m => m.BookingId == request.BookingId, Relay);
        Assert.Equal(0, await ReadAsync(c => c.PaymentEvents.CountAsync(e => e.BookingId == request.BookingId)));
    }

    [Fact]
    public async Task Settling_through_TransactionBehavior_relays_BookingPaid_and_leaves_no_envelope_behind()
    {
        var checkout = await SeedExecutingCheckoutAsync();

        await ThroughTransactionBehaviorAsync(checkout.CheckoutId, c => c.SucceedOrder(checkout.OrderId(0)));

        await fixture.Outcomes.WaitForAsync<BookingPaidIntegrationEvent>(m => m.BookingId == checkout.BookingId, Relay);
        var stored = await ReadAsync(c => c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId));
        Assert.True(stored.IsPaymentDone);
        Assert.True(stored.Order(checkout.OrderId(0)).WalletUpdated);
        await WaitForAsync(async () => await StagedEnvelopesAsync(nameof(BookingPaidIntegrationEvent)) == 0,
            "the sent BookingPaid envelope to be deleted from the outbox");
    }

    [Fact]
    public async Task Failing_an_order_relays_BookingPaymentFailed()
    {
        var checkout = await SeedExecutingCheckoutAsync();

        await ThroughTransactionBehaviorAsync(checkout.CheckoutId, c => c.FailOrder(checkout.OrderId(0)));

        await fixture.Outcomes.WaitForAsync<BookingPaymentFailedIntegrationEvent>(
            m => m.BookingId == checkout.BookingId, Relay);
    }

    [Fact]
    public async Task A_rolled_back_settlement_takes_its_BookingPaid_with_it()
    {
        var checkout = await SeedExecutingCheckoutAsync();
        var orderId = checkout.OrderId(0);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                (await context.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId)).SucceedOrder(orderId);
                await context.SaveChangesAsync();

                // Seen from inside the transaction: settlement ran and staged the message beside its writes.
                Assert.Equal(1, await context.Wallets.CountAsync(w => w.OwnerId == checkout.Order(orderId).MerchantId));
                Assert.True(await StagedEnvelopesAsync(context, nameof(BookingPaidIntegrationEvent)) > 0);

                await transaction.RollbackAsync();
            }

            // The same scope commits something else afterwards: a publisher that kept the rolled-back message
            // for the next commit would send it now.
            await using (var next = await context.Database.BeginTransactionAsync())
            {
                context.Wallets.Add(Wallet.Create(Guid.NewGuid(), "USD"));
                await context.SaveChangesAsync();
                await next.CommitAsync();
            }
        }

        var stored = await ReadAsync(c => c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId));
        Assert.Equal(PaymentOrderStatus.Executing, stored.Order(orderId).Status);
        Assert.False(stored.Order(orderId).WalletUpdated);
        Assert.False(stored.IsPaymentDone);
        Assert.Equal(0, await ReadAsync(c => c.Wallets.CountAsync(w => w.OwnerId == checkout.Order(orderId).MerchantId)));
        Assert.Equal(0, await ReadAsync(c => c.LedgerEntries.CountAsync(e => e.PaymentOrderId == orderId)));
        Assert.Equal(0, await StagedEnvelopesAsync(nameof(BookingPaidIntegrationEvent)));

        await Task.Delay(Silence);
        Assert.Empty(fixture.Outcomes.For<BookingPaidIntegrationEvent>(m => m.BookingId == checkout.BookingId));
    }

    private static long NewBookingId() => Random.Shared.NextInt64(1, long.MaxValue);

    private Endpoint[] BrokerEndpoints() =>
        fixture.Services.GetRequiredService<IWolverineRuntime>().Options.Transports.AllEndpoints()
            .Where(endpoint => endpoint.Uri.Scheme == "rabbitmq" && endpoint.Role == EndpointRole.Application)
            .ToArray();

    private async Task<PaymentEvent> SeedExecutingCheckoutAsync()
    {
        var checkout = CheckoutSeed.New();
        checkout.StartExecuting(checkout.OrderId(0), CheckoutSeed.Provider, CheckoutSeed.Token);
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        context.PaymentEvents.Add(checkout);
        await context.SaveChangesAsync();
        return checkout;
    }

    // The production TransactionBehavior around a webhook-shaped change, on a scope of the real host.
    private async Task ThroughTransactionBehaviorAsync(Guid checkoutId, Action<PaymentEvent> change)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var behavior = new TransactionBehavior<WebhookLikeCommand, Result>(context);

        var result = await behavior.Handle(new WebhookLikeCommand(), async cancellationToken =>
        {
            change(await context.PaymentEvents.SingleAsync(e => e.CheckoutId == checkoutId, cancellationToken));
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private async Task<T> ReadAsync<T>(Func<PaymentDbContext, Task<T>> read)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<PaymentDbContext>());
    }

    private async Task<PaymentEvent> WaitForCheckoutAsync(long bookingId)
    {
        PaymentEvent? found = null;
        await WaitForAsync(async () =>
        {
            found = await ReadAsync(c => c.PaymentEvents.SingleOrDefaultAsync(e => e.BookingId == bookingId));
            return found is not null;
        }, $"a checkout for booking {bookingId}");
        return found!;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + Relay;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    // Wolverine keeps a scheduled local message in its incoming table until it is due. Matched on the body too,
    // since every test's checkout schedules one.
    private async Task<DateTimeOffset[]> ScheduledExpiriesAsync(Guid checkoutId, PaymentDbContext? context = null)
    {
        var sql = $"""
                   select execution_time from "{await EnvelopeSchemaAsync("wolverine_incoming_envelopes")}".wolverine_incoming_envelopes
                   where message_type = 'checkout-expiry-due' and status = 'Scheduled'
                     and position(convert_to(@checkout, 'UTF8') in body) > 0
                   """;
        NpgsqlConnection? own = null;
        try
        {
            NpgsqlCommand command;
            if (context is null)
            {
                own = new NpgsqlConnection(fixture.ConnectionString);
                await own.OpenAsync();
                command = new NpgsqlCommand(sql, own);
            }
            else
            {
                command = new NpgsqlCommand(sql, (NpgsqlConnection)context.Database.GetDbConnection(),
                    (NpgsqlTransaction)context.Database.CurrentTransaction!.GetDbTransaction());
            }

            await using (command)
            {
                command.Parameters.AddWithValue("checkout", checkoutId.ToString());
                var due = new List<DateTimeOffset>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    due.Add(new DateTimeOffset(reader.GetDateTime(0), TimeSpan.Zero));
                return [.. due];
            }
        }
        finally
        {
            if (own is not null)
                await own.DisposeAsync();
        }
    }

    private async Task<string> EnvelopeSchemaAsync(string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var lookup = new NpgsqlCommand(
            "select table_schema from information_schema.tables where table_name = @table", connection);
        lookup.Parameters.AddWithValue("table", table);
        return (string)(await lookup.ExecuteScalarAsync())!;
    }

    private async Task<long> StagedEnvelopesAsync(string messageType)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(await CountStagedSqlAsync(), connection);
        command.Parameters.AddWithValue("type", $"%{messageType}%");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // Through the context's own connection, so it sees what the open transaction staged.
    private async Task<long> StagedEnvelopesAsync(PaymentDbContext context, string messageType)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(await CountStagedSqlAsync(), connection,
            (NpgsqlTransaction)context.Database.CurrentTransaction!.GetDbTransaction());
        command.Parameters.AddWithValue("type", $"%{messageType}%");
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // Looked up rather than assumed: the envelope tables live in Wolverine's own schema, not EF's.
    private async Task<string> CountStagedSqlAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var lookup = new NpgsqlCommand(
            "select table_schema from information_schema.tables where table_name = 'wolverine_outgoing_envelopes'",
            connection);
        var schema = (string)(await lookup.ExecuteScalarAsync())!;
        return $"select count(*) from \"{schema}\".wolverine_outgoing_envelopes where message_type like @type";
    }

    private sealed record WebhookLikeCommand : IRequest<Result>, ITransactionalRequest;
}
