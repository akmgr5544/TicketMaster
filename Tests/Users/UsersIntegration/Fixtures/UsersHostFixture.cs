using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace UsersIntegration.Fixtures;

// Boots the real Users host — Program.cs unmodified, its own JWT bearer validation and ApplyMigrationsAsync
// included — against a Postgres container, so roles are proved through the tokens the service really issues
// and really validates.
public sealed class UsersHostFixture : IAsyncLifetime
{
    // HS512 refuses a key shorter than 64 bytes.
    private const string SigningKey = "users-integration-signing-key-0123456789abcdef0123456789abcdef0123456789";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private WebApplicationFactory<Program>? _factory;
    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    public IServiceProvider Services =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).Services;

    public string ConnectionString => _postgres.GetConnectionString();

    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).CreateClient();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Environment variables, not WebApplicationFactory's hooks: Program.cs reads the signing key while
        // composing the builder, before those hooks run.
        Environment.SetEnvironmentVariable("ConnectionStrings__UsersDatabase", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("AuthConfigs__Token", SigningKey);

        _factory = new WebApplicationFactory<Program>();

        // Resolving forces the host to build and start — including Program.cs's own ApplyMigrationsAsync, so
        // the schema is the migrated one, never EnsureCreated. A startup failure surfaces here, named.
        _ = _factory.Services.GetService(typeof(IServiceProvider));

        _respawnConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _respawnConnection.OpenAsync();

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

        await _postgres.DisposeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__UsersDatabase", null);
        Environment.SetEnvironmentVariable("AuthConfigs__Token", null);
    }
}

[CollectionDefinition(Name)]
public sealed class UsersHostCollection : ICollectionFixture<UsersHostFixture>
{
    public const string Name = "Users host";
}
