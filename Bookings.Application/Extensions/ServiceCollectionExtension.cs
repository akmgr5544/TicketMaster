using Bookings.Application.Pipelines;
using Bookings.Application.Services.Implementations;
using Bookings.Application.Services.Interfaces;
using Medallion.Threading;
using Medallion.Threading.Redis;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Wolverine;
using Wolverine.RabbitMQ;

namespace Bookings.Application.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis");

        services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection!));
        services.AddScoped<IDatabase>(serviceProvider =>
            serviceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.ConnectionMultiplexerFactory = 
                () => Task.FromResult(services.BuildServiceProvider().GetRequiredService<IConnectionMultiplexer>());
        });
        services.AddSingleton<IDistributedLockProvider>(serviceProvider =>
        {
            var redisDb = serviceProvider.GetRequiredService<IDatabase>();
            return new RedisDistributedSynchronizationProvider(redisDb);
        });

        services.AddScoped<ICacheService, CacheService>();
        services.AddMediatR(cf =>
            cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }

    public static void ConfigureRabbitMq(this IHostBuilder hostBuilder)
    {
        hostBuilder.UseWolverine(options =>
        {
            options.UseRabbitMqUsingNamedConnection("")
                .AutoProvision()
                .UseConventionalRouting();
        });
    }
}