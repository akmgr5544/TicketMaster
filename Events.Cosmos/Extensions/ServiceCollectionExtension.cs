using Events.Application.IntegrationEvents;
using Events.Cosmos.IntegrationEvents;
using Events.Cosmos.Options;
using Events.Cosmos.Repositories;
using Events.Cosmos.Serialization;
using Events.Domain.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Events.Cosmos.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CosmosOptions>(configuration.GetSection(CosmosOptions.SectionName));

        // CosmosClient owns the connection pool and is thread-safe: exactly one per application.
        services.AddSingleton<CosmosClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CosmosOptions>>().Value;

            var clientOptions = new CosmosClientOptions
            {
                // Mutually exclusive with Serializer/SerializerOptions — setting either alongside
                // this throws.
                UseSystemTextJsonSerializerWithOptions = CosmosJson.Options
            };

            // Only the emulator sets this (see CosmosOptions.ConnectionMode). LimitToEndpoint travels
            // with Gateway mode because the emulator advertises internal replica addresses the client
            // otherwise tries — and fails — to reach.
            if (options.ConnectionMode is { } connectionMode)
            {
                clientOptions.ConnectionMode = connectionMode;
                clientOptions.LimitToEndpoint = true;
            }

            return new CosmosClient(options.ConnectionString, clientOptions);
        });

        services.AddSingleton<EventsCosmosContext>();

        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPerformerRepository, PerformerRepository>();
        
        services.AddScoped<IIntegrationEventDispatcher, CosmosOutboxDispatcher>();

        return services;
    }
    
    public static Task EnsureContainersAsync(this IHost app, CancellationToken cancellationToken = default) =>
        app.Services.EnsureContainersAsync(cancellationToken);

    // Provider-based core so the integration fixture provisions through the exact same path the host
    // does, rather than hand-copying it and letting the two drift.
    public static async Task EnsureContainersAsync(this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<CosmosClient>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<CosmosOptions>>().Value;

        var database = await client.CreateDatabaseIfNotExistsAsync(options.Database,
            options.Throughput,
            cancellationToken: cancellationToken);

        foreach (var container in CosmosContainers.All)
        {
            await database.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(container, CosmosContainers.PartitionKeyPath),
                cancellationToken: cancellationToken);
        }
    }
}
