using Microsoft.EntityFrameworkCore;
using Notifications.Data;
using Notifications.Email;

namespace Notifications.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddNotificationsServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("NotificationsDatabase")));

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Host) && !string.IsNullOrWhiteSpace(options.From),
                "Email requires Host and From.")
            .ValidateOnStart();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }

    // Each service creates and migrates its own database on first boot; EF Core locks around it.
    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    }
}
