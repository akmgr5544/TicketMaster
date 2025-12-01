using Events.Application.Pipelines;
using MassTransit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Events.Application.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        
        services.AddMediatR(cf => 
            cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        return services;
    }
}