using Events.Domain.Repositories;
using Events.Mongo.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Events.Mongo.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<MongoClient>(provider =>
        {
            //TODO:: add more configs
            return new MongoClient("");
        });

        services.AddSingleton<IMongoDatabase>(provider =>
        {
            var client = provider.GetRequiredService<MongoClient>();
            //TODO:: add config model
            return client.GetDatabase("");
        });

        MongoCollectionConfig.AddMongoCollectionConfig();
        
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPerformerRepository, PerformerRepository>();
        return services;
    }
}