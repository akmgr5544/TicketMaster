using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Extensions;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Wolverine;

namespace PaymentIntegration.Fixtures;

// The production composition — AddInfrastructureServices — against a real Postgres. Wolverine is not started here
// (ConfigureMessaging is a host-builder extension, exactly as in Bookings), so the two hops it would own are
// replaced: the PSP by StubPsp, and IMessageBus by a recorder. What Wolverine itself guarantees — the transaction
// around a message handler, the outbox, the broker — is proven by PaymentsHostFixture instead.
public sealed class PaymentsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    public ServiceProvider Services { get; private set; } = null!;

    public ControllableTimeProvider Clock { get; } = new();

    public StubPsp Psp => Services.GetRequiredService<StubPsp>();

    public MessageBusLog Bus { get; } = new();

    public LogCapture Logs => Services.GetRequiredService<LogCapture>();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                // Never dialled: StubPsp replaces the factory. AddPaymentProviders still registers its options.
                ["PaymentProviders:Default"] = "Stripe",
                ["PaymentProviders:Stripe:SecretKey"] = "sk_test_unused",
                ["PaymentProviders:Stripe:WebhookSecret"] = "whsec_unused"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddInfrastructureServices(configuration);

        // Registered after AddInfrastructureServices so it wins over TimeProvider.System.
        services.AddSingleton<TimeProvider>(Clock);
        services.AddPspStubs();
        services.AddSingleton(Bus);
        services.RemoveAll<IMessageBus>();
        services.AddScoped(_ => RecordingMessageBus.Create(Bus));

        Services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        await using (var scope = Services.CreateAsyncScope())
        {
            // Migrations, never EnsureCreated: the schema under test is the one production gets, hand-written
            // wallet index included.
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
        Psp.Reset();
        Bus.Reset();
        Logs.Clear();
    }

    public async Task DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
