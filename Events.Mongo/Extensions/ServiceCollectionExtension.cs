using Events.Domain.Repositories;
using Events.Mongo.Options;
using Events.Mongo.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Events.Mongo.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection("MongoConfigs"));

        services.TryAddSingleton<IMongoClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
            return new MongoClient(options.ConnectionString);
        });

        services.AddSingleton<IMongoDatabase>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
            var client = provider.GetRequiredService<MongoClient>();
            return client.GetDatabase(options.Database);
        });

        services.AddSingleton<MongoDomainContext>();
        MongoCollectionConfig.AddMongoCollectionConfig();

        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPerformerRepository, PerformerRepository>();
        return services;
    }
}