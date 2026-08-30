using Bookings.Domain.Abstractions;
using Bookings.Domain.Repositories;
using Bookings.Sql.Interceptors;
using Bookings.Sql.Pipelines;
using Bookings.Sql.Repositories;
using MediatR;
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
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        services.AddScoped<IAfterCommitQueue, AfterCommitQueue>();

        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<ITicketsRepository, TicketsRepository>();

        // Scoped, not singleton. The interceptor holds an IPublisher, and a singleton would capture one
        // resolved from the root container — putting every domain event handler on its own DbContext,
        // outside the transaction the caller is saving inside. A rolled-back booking then leaves its
        // tickets Booked and the seat stranded.
        services.AddScoped<DomainEventPublisherInterceptor>();

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