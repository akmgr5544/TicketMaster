using Events.Application.Pipelines;
using MassTransit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.RabbitMQ;

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
    
    public static void ConfigureRabbitMq(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseWolverine(options =>
        {
            options.UseRabbitMqUsingNamedConnection("")
                .AutoProvision()
                .UseConventionalRouting();
            
            options.Policies.DisableConventionalLocalRouting();
        });
    }
}