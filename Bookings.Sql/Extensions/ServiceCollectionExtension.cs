using Bookings.Domain.Repositories;
using Bookings.Sql.Interceptors;
using Bookings.Sql.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    
    public static async Task ApplyMigrationsAsync(this IHost app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();
        await dbContext.Database.MigrateAsync();
    }
}