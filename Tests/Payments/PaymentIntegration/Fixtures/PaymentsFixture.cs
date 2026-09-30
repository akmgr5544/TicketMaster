using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Domain.Events;
using PaymentSystem.Extensions;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace PaymentIntegration.Fixtures;

public sealed class PaymentsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    public ServiceProvider Services { get; private set; } = null!;

    public ControllableTimeProvider Clock { get; } = new();

    public DomainEventRecorder Events { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddInfrastructureServices(configuration);

        // Registered after AddInfrastructureServices so it wins over TimeProvider.System; everything else
        // is the production wiring.
        services.AddSingleton<TimeProvider>(Clock);

        // The PSP is another process: only its factory is replaced.
        services.AddPspStubs();

        services.AddSingleton(Events);
        services.AddScoped<RecordingHandler>();
        services.AddScoped<INotificationHandler<PaymentOrderSucceededDomainEvent>>(sp => sp.GetRequiredService<RecordingHandler>());
        services.AddScoped<INotificationHandler<PaymentOrderFailedDomainEvent>>(sp => sp.GetRequiredService<RecordingHandler>());

        // The production publisher is registered only with Wolverine (ConfigureMessaging); see PaymentsHostFixture.
        services.AddSingleton<IntegrationEventLog>();
        services.AddScoped<PaymentSystem.Shared.Messaging.IIntegrationEventPublisher, RecordingIntegrationEventPublisher>();

        Services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        await using (var scope = Services.CreateAsyncScope())
        {
            // Migrations, never EnsureCreated: the schema under test is the one production gets, so a migration
            // that drifts from the model fails here instead of at deployment.
            await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
        }

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
        Clock.Reset();
        Events.Reset();
    }

    public async Task DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
