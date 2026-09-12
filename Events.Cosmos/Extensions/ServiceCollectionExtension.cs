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

            return new CosmosClient(options.ConnectionString, new CosmosClientOptions
            {
                // Mutually exclusive with Serializer/SerializerOptions — setting either alongside
                // this throws.
                UseSystemTextJsonSerializerWithOptions = CosmosJson.Options
            });
        });

        services.AddSingleton<EventsCosmosContext>();

        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPerformerRepository, PerformerRepository>();
        
        services.AddScoped<IIntegrationEventDispatcher, CosmosOutboxDispatcher>();

        return services;
    }
    
    public static async Task EnsureContainersAsync(this IHost app, CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();

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
