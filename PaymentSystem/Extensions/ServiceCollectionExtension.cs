using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Data.Interceptors;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Pipelines;

namespace PaymentSystem.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(cf => cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));
        // Constrained to ITransactionalRequest, so DI skips it for queries.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddExceptionHandler<DomainExceptionHandler>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditTimestampsInterceptor>();
        // Scoped, not singleton: it holds an IPublisher, and one from the root container would run domain event
        // handlers on their own DbContext, outside the transaction of the save that raised them.
        services.AddScoped<DomainEventPublisherInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<PaymentDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(
                serviceProvider.GetRequiredService<AuditTimestampsInterceptor>(),
                serviceProvider.GetRequiredService<DomainEventPublisherInterceptor>());
        });

        services.AddDbContext<PaymentQueryDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }

    // A new slice implements IEndpointMarker and is mapped; nothing is registered by hand.
    public static IServiceCollection AddFeatureEndpoints(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(ServiceCollectionExtension).Assembly)
            .AddClasses(classes => classes.AssignableTo<IEndpointMarker>())
            .AsImplementedInterfaces()
            .WithSingletonLifetime());
        return services;
    }

    public static void MapFeatureEndpoints(this WebApplication app)
    {
        foreach (var endpoint in app.Services.GetServices<IEndpointMarker>())
            endpoint.MapEndpoint(app);
    }
}
