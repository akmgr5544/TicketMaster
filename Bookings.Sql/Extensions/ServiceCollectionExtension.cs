using Bookings.Domain.Repositories;
using Bookings.Sql.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Bookings.Sql.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<ITicketsRepository, TicketsRepository>();

        services.AddDbContext<BookingDomainContext>(options =>
        {

        });

        return services;
    }
}