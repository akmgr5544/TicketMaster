using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Extensions;
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
        services.AddMediatR(cf =>
            cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AggregateVersionInterceptor>();
        services.AddSingleton<AuditTimestampsInterceptor>();
        // Scoped, not singleton: it holds an IPublisher, and one resolved from the root container would run
        // every domain event handler on its own DbContext, outside the transaction of the save that raised it.
        services.AddScoped<DomainEventPublisherInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<PaymentDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);
            // Version before timestamps: bumping the version is what marks an order-only change's checkout
            // row modified, and only then does the audit interceptor see it and stamp its UpdatedAt.
            options.AddInterceptors(
                serviceProvider.GetRequiredService<AggregateVersionInterceptor>(),
                serviceProvider.GetRequiredService<AuditTimestampsInterceptor>(),
                serviceProvider.GetRequiredService<DomainEventPublisherInterceptor>());
        });

        services.AddPaymentProviders(configuration);

        return services;
    }

    // Endpoints are discovered, never hand-registered: a new slice implements IEndpointMarker and is mapped.
    public static IServiceCollection AddFeatureEndpoints(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblies(typeof(ServiceCollectionExtension).Assembly)
            .AddClasses(classes => classes.AssignableTo<IEndpointMarker>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());
        return services;
    }

    // A single-instance convenience, as in Bookings and Users: two instances booting together race on it.
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
    }

    public static void MapFeatureEndpoints(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        foreach (var endpoint in scope.ServiceProvider.GetServices<IEndpointMarker>())
            endpoint.MapEndpoint(app);
    }
}
