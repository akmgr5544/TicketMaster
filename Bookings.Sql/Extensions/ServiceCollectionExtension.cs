using Bookings.Domain.Repositories;
using Bookings.Sql.Interceptors;
using Bookings.Sql.Repositories;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Bookings.Sql.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<ITicketsRepository, TicketsRepository>();

        services.AddSingleton<DomainEventPublisherInterceptor>();

        services.AddDbContext<BookingDomainContext>((serviceProvider, options) =>
        {
            var interceptor = serviceProvider.GetRequiredService<DomainEventPublisherInterceptor>();
            options.AddInterceptors(interceptor);
        });

        return services;
    }
}