using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.RabbitMQ;

namespace UsersIntegration.Fixtures;

// Boots the real Users host — Program.cs unmodified, its own JWT bearer validation, ApplyMigrationsAsync and
// Wolverine messaging included — against Postgres and RabbitMQ containers, so roles are proved through the tokens
// the service really issues and validates, and what it publishes is proved by a consumer on the real broker.
public sealed class UsersHostFixture : IAsyncLifetime
{
    // HS512 refuses a key shorter than 64 bytes.
    private const string SigningKey = "users-integration-signing-key-0123456789abcdef0123456789abcdef0123456789";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    private WebApplicationFactory<Program>? _factory;
    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    // A minimal Wolverine host on the same broker standing in for PaymentSystem, recording what Users publishes.
    private IHost? _subscriber;

    public IServiceProvider Services =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).Services;

    public string ConnectionString => _postgres.GetConnectionString();

    public PublishedUsers Published { get; } = new();

    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).CreateClient();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        // The subscriber starts first so its queue is bound before Users publishes anything.
        _subscriber = Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.UseRabbitMq(new Uri(_rabbit.GetConnectionString()))
                    .AutoProvision()
                    .UseConventionalRouting();

                // Only the probe: scanning this test assembly could pick up anything shaped like a handler.
                options.Discovery.DisableConventionalDiscovery().IncludeType<UserRegisteredProbe>();
                options.Services.AddSingleton(Published);
            })
            .Build();
        await _subscriber.StartAsync();

        // Environment variables, not WebApplicationFactory's hooks: Program.cs reads the signing key and the
        // connection strings while composing the builder, before those hooks run.
        Environment.SetEnvironmentVariable("ConnectionStrings__UsersDatabase", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", _rabbit.GetConnectionString());
        Environment.SetEnvironmentVariable("AuthConfigs__Token", SigningKey);

        _factory = new WebApplicationFactory<Program>();

        // Resolving forces the host to build and start — including Program.cs's own ApplyMigrationsAsync, so
        // the schema is the migrated one, never EnsureCreated. A startup failure surfaces here, named.
        _ = _factory.Services.GetService(typeof(IServiceProvider));

        _respawnConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _respawnConnection.OpenAsync();

        // Only the public schema: Wolverine keeps its envelope tables in its own and must not lose them mid-run.
        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("__EFMigrationsHistory")]
        });
    }

    public Task ResetAsync() => _respawner.ResetAsync(_respawnConnection);

    public async Task DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();

        if (_factory is not null)
            await _factory.DisposeAsync();

        if (_subscriber is not null)
            await _subscriber.StopAsync();

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());

        Environment.SetEnvironmentVariable("ConnectionStrings__UsersDatabase", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", null);
        Environment.SetEnvironmentVariable("AuthConfigs__Token", null);
    }
}

[CollectionDefinition(Name)]
public sealed class UsersHostCollection : ICollectionFixture<UsersHostFixture>
{
    public const string Name = "Users host";
}

public sealed class UserRegisteredProbe
{
    public void Consume(UserRegisteredIntegrationEvent message, PublishedUsers published) => published.Add(message);
}

// What reached the stand-in subscriber off the broker. Each test registers its own email, so it waits for the
// message about its user and ignores the rest.
public sealed class PublishedUsers
{
    private readonly ConcurrentQueue<UserRegisteredIntegrationEvent> _received = new();

    public void Add(UserRegisteredIntegrationEvent message) => _received.Enqueue(message);

    public IReadOnlyList<UserRegisteredIntegrationEvent> For(string email) =>
        _received.Where(message => message.Email == email).ToArray();

    public async Task<UserRegisteredIntegrationEvent> WaitForAsync(string email, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (For(email).FirstOrDefault() is { } found)
                return found;
            await Task.Delay(100);
        }

        throw new TimeoutException($"No UserRegistered for {email} arrived within {timeout.TotalSeconds:0}s.");
    }
}
