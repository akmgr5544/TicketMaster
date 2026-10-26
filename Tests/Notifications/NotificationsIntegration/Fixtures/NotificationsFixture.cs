using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notifications.Data;
using Notifications.Email;
using Notifications.Extensions;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace NotificationsIntegration.Fixtures;

// The production registration (AddNotificationsServices) against a real Postgres. Wolverine is not started here —
// ConfigureMessaging is a host-builder extension — and the mail server is another process, so the email sender is the
// one hop replaced. The transaction, the outbox and the broker are proven by NotificationsHostFixture.
public sealed class NotificationsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    public ServiceProvider Services { get; private set; } = null!;

    public RecordingEmailSender Emails { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NotificationsDatabase"] = _postgres.GetConnectionString(),
                ["Email:Host"] = "unused",
                ["Email:From"] = "no-reply@test.local"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddNotificationsServices(configuration);
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(Emails);

        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();

        _respawnConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _respawnConnection.OpenAsync();
        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Table("__EFMigrationsHistory")]
        });
    }

    public async Task ResetAsync()
    {
        await _respawner.ResetAsync(_respawnConnection);
        Emails.Reset();
    }

    public async Task DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class NotificationsCollection : ICollectionFixture<NotificationsFixture>
{
    public const string Name = "Notifications";
}

public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyCollection<SentEmail> Sent => _sent;

    // Set to make the next sends fail as an unreachable mail server would.
    public bool Fail { get; set; }

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (Fail)
            throw new EmailDeliveryException("The mail server is down.", new IOException("connection refused"));
        _sent.Enqueue(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }

    public void Reset()
    {
        _sent.Clear();
        Fail = false;
    }
}

public sealed record SentEmail(string To, string Subject, string Body);
