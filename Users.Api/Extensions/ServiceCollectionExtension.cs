using Microsoft.EntityFrameworkCore;
using Users.Api.Database;
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

    public static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<UsersDomainContext>(options =>
        {
            options.UseNpgsql(configuration.GetConnectionString("UsersDatabase"));
            //More configurations
        });
    }

    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<UsersDomainContext>();
        await dbContext.Database.MigrateAsync();
    }
}