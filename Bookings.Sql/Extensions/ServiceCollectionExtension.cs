using Bookings.Domain.Repositories;
using Bookings.Sql.Interceptors;
using Bookings.Sql.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bookings.Sql.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<ITicketsRepository, TicketsRepository>();

        services.AddSingleton<DomainEventPublisherInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")!;
        services.AddDbContext<BookingDomainContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);
            var interceptor = serviceProvider.GetRequiredService<DomainEventPublisherInterceptor>();
            options.AddInterceptors(interceptor);
        });

        return services;
    }
}