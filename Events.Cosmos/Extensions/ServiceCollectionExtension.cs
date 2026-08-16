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

        return services;
    }

    /// <summary>
    /// Creates the database and containers on startup, mirroring how Bookings applies migrations.
    /// <para>
    /// This provisions; it does not migrate. <c>CreateContainerIfNotExistsAsync</c> matches on the
    /// container id alone, so an existing container is returned untouched and later changes to a
    /// partition key or indexing policy are silently ignored here — those need
    /// <c>ReplaceContainerAsync</c> or a new container.
    /// </para>
    /// </summary>
    public static async Task EnsureContainersAsync(this IHost app, CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<CosmosClient>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<CosmosOptions>>().Value;

        // Throughput is provisioned once at the database level and shared by every container, so
        // three containers cost the same floor as one.
        var database = await client.CreateDatabaseIfNotExistsAsync(options.Database,
            options.Throughput,
            cancellationToken: cancellationToken);

        foreach (var container in CosmosContainers.All)
        {
            // Default indexing is left in place. Cosmos indexes everything including geospatial
            // data, and this catalogue is written rarely and read often, so the write cost of a
            // broad index is a fair trade for never having to add one later.
            await database.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(container, CosmosContainers.PartitionKeyPath),
                cancellationToken: cancellationToken);
        }
    }
}
