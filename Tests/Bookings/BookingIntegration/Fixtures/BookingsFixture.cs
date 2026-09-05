using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.DomainEvents;
using Bookings.Sql;
using Bookings.Sql.Extensions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Respawn.Graph;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace BookingIntegration.Fixtures;

public sealed class BookingsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                // allowAdmin is what lets ResetAsync issue FLUSHDB. Production never needs it, so it
                // is added to the test connection string rather than to AddApplicationServices.
                ["ConnectionStrings:Redis"] = $"{_redis.GetConnectionString()},allowAdmin=true",
                // Never dialled: IEventsService is stubbed below. Present because
                // AddApplicationServices requires it, so the production wiring still runs as written.
                ["Services:Events:GrpcAddress"] = "https://events.invalid"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // The production wiring, called for real. ConfigureRabbitMq is an IHostBuilder extension and
        // is deliberately not called, which keeps Wolverine and RabbitMQ out without stubbing.
        services.AddInfrastructureServices(configuration);
        services.AddApplicationServices(configuration);

        // Replaces the gRPC-backed EventsService registered by AddApplicationServices. Registered
        // after it so this wins. See StubEventsService.
        services.AddScoped<StubEventsService>();
        services.AddScoped<IEventsService>(sp => sp.GetRequiredService<StubEventsService>());

        // Test-only observability hook: a second handler for BookingCreatedDomainEvent so mechanics
        // tests can assert on publish counts directly, without a hand-built IPublisher that would also
        // mean a hand-built, off-transaction context. See BookingCreatedPublishCounter.
        services.AddScoped<BookingCreatedPublishCounter>();
        services.AddScoped<INotificationHandler<BookingCreatedDomainEvent>>(
            sp => sp.GetRequiredService<BookingCreatedPublishCounter>());

        // Test-only injection hook: registered after AddInfrastructureServices so MediatR nests it
        // inside TransactionBehavior, letting a test fail a request after its handler has already run
        // and queued after-commit work, but before the transaction commits. See
        // AfterHandlerFailureBehavior.
        services.AddScoped<AfterHandlerFailureSwitch>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AfterHandlerFailureBehavior<,>));

        // ValidateScopes catches the exact shape of bug this suite exists for: DomainEventPublisherInterceptor
        // was once registered AddSingleton while holding IPublisher, which resolved every domain-event
        // handler's scoped BookingDomainContext from the root container - outside the caller's transaction -
        // and stranded seats on any rollback. ValidateScopes checks every resolution against the root
        // provider at runtime, so a regression of that shape fails immediately, naming the offending
        // service, instead of surfacing as a handful of confusing behavioural failures. ValidateOnBuild
        // catches the same class of mistake earlier, at container-build time, for the cases it can see
        // statically.
        Services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        await using (var scope = Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();
            await context.Database.MigrateAsync();
        }

        _respawnConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _respawnConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            // Truncating this makes EF believe no migration has been applied, and the next test meets
            // an empty schema.
            TablesToIgnore = [new Table("__EFMigrationsHistory")]
        });
    }

    public async Task ResetAsync()
    {
        await _respawner.ResetAsync(_respawnConnection);

        var multiplexer = Services.GetRequiredService<IConnectionMultiplexer>();

        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            await multiplexer.GetServer(endpoint).FlushDatabaseAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
