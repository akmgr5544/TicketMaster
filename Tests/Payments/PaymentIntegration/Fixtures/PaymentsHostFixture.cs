using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PaymentSystem.Data;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.RabbitMQ;

namespace PaymentIntegration.Fixtures;

// Boots the real PaymentSystem host — Program.cs unmodified, ConfigureMessaging included — against its own
// Postgres and RabbitMQ containers. The one place Wolverine and the real outbox publisher run: PaymentsFixture
// composes a plain ServiceProvider with a recording publisher, so nothing there can observe a message
// actually leaving, or not leaving, with its transaction.
public sealed class PaymentsHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    // A second, minimal Wolverine host on the same broker standing in for Bookings: it publishes
    // PaymentRequested and records the payment outcomes Payments sends back.
    private IHost? _bookings;

    public IServiceProvider Services =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).Services;

    public IMessageBus Bookings =>
        (_bookings ?? throw new InvalidOperationException("The probe was not started.")).Services
        .GetRequiredService<IMessageBus>();

    public string ConnectionString => _postgres.GetConnectionString();

    public PaymentOutcomes Outcomes { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        // Migrated here rather than by Program.cs: WebApplicationFactory takes over the host at Build(), so the
        // startup ApplyMigrationsAsync call after it never runs under the factory.
        await using (var context = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
                         .UseNpgsql(_postgres.GetConnectionString()).Options))
        {
            await context.Database.MigrateAsync();
        }

        // Environment variables, not WebApplicationFactory's hooks: Program.cs reads the connection strings
        // while composing the builder, before those hooks run. Safe against PaymentsFixture, which builds an
        // explicit in-memory configuration and never reads the environment.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", _rabbit.GetConnectionString());
        // Never dialled; the provider options refuse to start without them.
        Environment.SetEnvironmentVariable("PaymentProviders__Stripe__SecretKey", "sk_test_dummy");
        Environment.SetEnvironmentVariable("PaymentProviders__Stripe__WebhookSecret", "whsec_dummy");

        _factory = new WebApplicationFactory<Program>();

        // Resolving forces the host to build and start, so a startup failure surfaces here, named.
        _ = _factory.Services.GetService(typeof(IServiceProvider));

        _bookings = Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                    .AutoProvision()
                    .UseConventionalRouting();

                // Only the probe: scanning this test assembly could pick up anything shaped like a handler.
                options.Discovery.DisableConventionalDiscovery().IncludeType<PaymentOutcomeProbe>();
                options.Services.AddSingleton(Outcomes);
            })
            .Build();
        await _bookings.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_bookings is not null)
            await _bookings.StopAsync();

        if (_factory is not null)
            await _factory.DisposeAsync();

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", null);
        Environment.SetEnvironmentVariable("PaymentProviders__Stripe__SecretKey", null);
        Environment.SetEnvironmentVariable("PaymentProviders__Stripe__WebhookSecret", null);
    }
}

[CollectionDefinition(Name)]
public sealed class PaymentsHostCollection : ICollectionFixture<PaymentsHostFixture>
{
    public const string Name = "Payments host";
}

public sealed class PaymentOutcomeProbe
{
    public void Consume(BookingPaidIntegrationEvent message, PaymentOutcomes outcomes) => outcomes.Add(message);

    public void Consume(BookingPaymentFailedIntegrationEvent message, PaymentOutcomes outcomes) => outcomes.Add(message);
}

// What arrived at the probe off the broker. Every test uses its own booking id, so a test waits for the
// message about its booking and ignores the rest.
public sealed class PaymentOutcomes
{
    private readonly ConcurrentQueue<object> _received = new();

    public void Add(object message) => _received.Enqueue(message);

    public IReadOnlyList<T> For<T>(Func<T, bool> match) => _received.OfType<T>().Where(match).ToArray();

    public async Task<T> WaitForAsync<T>(Func<T, bool> match, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (For(match).FirstOrDefault() is { } found)
                return found;
            await Task.Delay(100);
        }

        throw new TimeoutException($"No {typeof(T).Name} matching the test arrived within {timeout.TotalSeconds:0}s.");
    }
}
