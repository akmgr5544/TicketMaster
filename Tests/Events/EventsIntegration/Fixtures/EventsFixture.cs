using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Events.Application.Extensions;
using Events.Application.IntegrationEvents;
using Events.Cosmos;
using Events.Cosmos.Extensions;
using Events.Cosmos.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EventsIntegration.Fixtures;

public sealed class EventsFixture : IAsyncLifetime
{
    // Microsoft's well-known emulator account key — not a secret, the same one appsettings.Development
    // and compose.yaml already carry.
    private const string EmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private const string TestDatabase = "events_test";

    // vnext-latest is the only line with a native arm64 build (Apple Silicon); the classic `latest`
    // emulator is amd64-only. It serves cleartext http on 8081 and rejects the SDK's default Direct
    // mode — hence the http endpoint and Gateway mode below.
    private readonly IContainer _cosmos =
        new ContainerBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest")
        .WithPortBinding(8081, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("fully ready to accept requests"))
        .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _cosmos.StartAsync();

        var endpoint = $"http://localhost:{_cosmos.GetMappedPublicPort(8081)}/";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CosmosConfigs:ConnectionString"] = $"AccountEndpoint={endpoint};AccountKey={EmulatorKey}",
                ["CosmosConfigs:Database"] = TestDatabase,
                // The one test-only knob: production leaves this unset (Direct). See CosmosOptions.
                ["CosmosConfigs:ConnectionMode"] = nameof(ConnectionMode.Gateway)
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // The production wiring, called for real. ConfigureRabbitMq is a separate IHostBuilder
        // extension and is deliberately not called, which keeps Wolverine and RabbitMQ out.
        services.AddInfrastructureServices(configuration);
        services.AddApplicationServices();

        // Remove — not shadow — CosmosOutboxDispatcher: it depends on IWolverineRuntime, which the
        // no-broker fixture omits. Removing it is what lets ValidateOnBuild stay on below. A singleton
        // stub so dispatched events survive scope disposal and stay readable from the read-back scope.
        services.RemoveAll<IIntegrationEventDispatcher>();
        services.AddSingleton<StubIntegrationEventDispatcher>();
        services.AddSingleton<IIntegrationEventDispatcher>(
            sp => sp.GetRequiredService<StubIntegrationEventDispatcher>());

        Services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        await Services.EnsureContainersAsync();
    }

    // Cosmos has no Respawn adapter. The reset analogue: delete every document in each container. At
    // catalogue-size test data this is a handful of point deletes; containers (and their throughput
    // and indexes) stay provisioned from InitializeAsync.
    public async Task ResetAsync()
    {
        // The stub is a singleton and accumulates dispatched events; clear it alongside the store.
        Services.GetRequiredService<StubIntegrationEventDispatcher>().Clear();

        var client = Services.GetRequiredService<CosmosClient>();
        var database = client.GetDatabase(TestDatabase);

        foreach (var name in CosmosContainers.All)
        {
            var container = database.GetContainer(name);

            using var iterator = container.GetItemQueryIterator<string>("SELECT VALUE c.id FROM c");

            while (iterator.HasMoreResults)
            {
                foreach (var id in await iterator.ReadNextAsync())
                    await container.DeleteItemAsync<object>(id, new PartitionKey(id));
            }
        }
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _cosmos.DisposeAsync();
    }
}
