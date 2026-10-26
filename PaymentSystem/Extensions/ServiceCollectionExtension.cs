using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Extensions;
using PaymentSystem.Data;
using PaymentSystem.Data.Interceptors;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Features.RefundOrders;
using PaymentSystem.Features.Settlements;
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
        services.AddProblemDetails();
        services.AddExceptionHandler<DomainExceptionHandler>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditTimestampsInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<PaymentDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditTimestampsInterceptor>());
        });

        services.AddDbContext<PaymentQueryDbContext>(options => options.UseNpgsql(connectionString));

        // Validated at startup: the service does not boot without its PSP secrets.
        services.AddPaymentProviders(configuration);

        services.AddScoped<OrderReconciler>();
        services.AddScoped<RefundReconciler>();
        services.AddScoped<SettlementReconciler>();

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

    // As in Bookings and Users: each service creates and migrates its own database on first boot. EF Core takes a
    // lock around it, so two instances booting together do not both migrate.
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
    }
}
