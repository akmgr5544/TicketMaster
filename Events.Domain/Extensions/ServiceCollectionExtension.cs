using Microsoft.Extensions.DependencyInjection;

namespace Events.Domain.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        return services;
    }
}