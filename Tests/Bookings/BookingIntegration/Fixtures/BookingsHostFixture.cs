using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace BookingIntegration.Fixtures;

/// <summary>
/// Boots the real Bookings host — <c>Program.cs</c> unmodified, <c>ConfigureRabbitMq</c> included —
/// against Postgres, Redis and RabbitMQ containers.
/// <para>
/// This is the one place Wolverine actually starts, and it is why the project has a second fixture.
/// <see cref="BookingsFixture"/> composes a plain <c>ServiceProvider</c> and never calls
/// <c>ConfigureRabbitMq</c> — that is what keeps 113 tests running in about a second — so nothing
/// there can observe broker wiring, a failed startup, or a durability policy that was never applied.
/// Its own containers, so neither fixture can disturb the other's database.
/// </para>
/// </summary>
public sealed class BookingsHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    public IServiceProvider Services =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).Services;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync());

        // Environment variables rather than WebApplicationFactory's configuration hooks: Program.cs
        // reads every connection string while composing the builder, which is before those hooks run.
        // The default configuration sources include environment variables, which are in place already.
        //
        // These are process-global, and this collection runs in parallel with the other one. It is
        // harmless only because BookingsFixture builds its configuration from an explicit in-memory
        // collection and never reads the environment. Adding AddEnvironmentVariables() there would
        // make these leak into it.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", _rabbit.GetConnectionString());
        // Never dialled; AddApplicationServices refuses to start without it.
        Environment.SetEnvironmentVariable("Services__Events__GrpcAddress", "https://events.invalid");

        _factory = new WebApplicationFactory<Program>();

        // Resolving forces the host to build and start. A startup failure surfaces here, named, rather
        // than as every test failing for no stated reason.
        _ = _factory.Services.GetService(typeof(IServiceProvider));
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class BookingsHostCollection : ICollectionFixture<BookingsHostFixture>
{
    public const string Name = "Bookings host";
}
