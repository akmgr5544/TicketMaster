using Events.Application.IntegrationEvents;
using Events.Application.Pipelines;
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
        // Registration order is pipeline order, outermost first. The retry has to be outside
        // everything else: it re-runs the whole request, so any behavior it wrapped inside would
        // only see one attempt, and a behavior registered outside it would see the retries as
        // separate requests.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ConcurrencyRetryBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // The one route to the broker. Scoped, because IMessageBus is.
        services.AddScoped<IIntegrationEventPublisher, WolverineIntegrationEventPublisher>();

        return services;
    }
    
    public static void ConfigureRabbitMq(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseWolverine(options =>
        {
            // Takes the connection string *name*; Wolverine resolves it from IConfiguration itself.
            options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                .AutoProvision()
                .UseConventionalRouting();
            
            options.Policies.DisableConventionalLocalRouting();
        });
    }
}