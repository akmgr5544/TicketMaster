using Users.Api.Options;
using Users.Api.Shared;

namespace Users.Api.Extensions;

public static class ServiceCollectionExtension
{
    public static void AddBusinessServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cf =>
            cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));

        services.Scan(scan => scan
            .FromAssemblyOf<Program>()
            .AddClasses(classes => classes.AssignableTo<IEndpointMarker>())
            .AsImplementedInterfaces()
            .WithScopedLifetime());
    }
    
}