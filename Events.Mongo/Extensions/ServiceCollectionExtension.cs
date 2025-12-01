using Events.Domain.Repositories;
using Events.Mongo.Repositories;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Events.Mongo.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
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
        
        services.AddMassTransit(config =>
        {
            config.AddMongoDbOutbox(options =>
            {
                options.QueryDelay = TimeSpan.FromSeconds(1);

                options.ClientFactory(provider => provider.GetRequiredService<IMongoClient>());
                options.DatabaseFactory(provider =>
                {
                    var mongoClient = provider.GetRequiredService<IMongoClient>();
                    
                    return mongoClient.GetDatabase("");
                });

                options.DuplicateDetectionWindow = TimeSpan.FromSeconds(30);
            });
        });
        //TODO:: add mongo configurations
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPerformerRepository, PerformerRepository>();
        return services;
    }
}