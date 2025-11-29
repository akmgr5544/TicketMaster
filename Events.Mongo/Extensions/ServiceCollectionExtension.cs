using Events.Domain.Repositories;
using Events.Mongo.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Events.Mongo.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        //TODO:: add mongo configurations
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();
        services.AddScoped<IPerformerRepository, PerformerRepository>();
        return services;
    }
}