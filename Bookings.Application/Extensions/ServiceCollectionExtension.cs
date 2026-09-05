using Bookings.Application.Services.Implementations;
using Bookings.Application.Services.Interfaces;
using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using TicketMaster.Common.Protos.Events.V1;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace Bookings.Application.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnection = configuration.GetConnectionString("Redis")
                              ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

        // Resolved lazily: connecting during registration blocks startup on Redis being reachable.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));

        // Singleton, not scoped. IDatabase is a cheap thread-safe handle over the multiplexer, and
        // the singleton IDistributedLockProvider below cannot depend on a scoped service.
        services.AddSingleton<IDatabase>(serviceProvider =>
            serviceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        services.AddSingleton<IDistributedLockProvider>(serviceProvider =>
        {
            var redisDb = serviceProvider.GetRequiredService<IDatabase>();
            return new RedisDistributedSynchronizationProvider(redisDb);
        });

        services.AddScoped<ICacheService, CacheService>();

        var eventsAddress = configuration["Services:Events:GrpcAddress"]
                            ?? throw new InvalidOperationException(
                                "'Services:Events:GrpcAddress' is not configured.");

        services.AddGrpcClient<EventsLookup.EventsLookupClient>(o => o.Address = new Uri(eventsAddress));
        services.AddScoped<IEventsService, EventsService>();

        services.AddMediatR(cf =>
            cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));
        return services;
    }

    public static void ConfigureRabbitMq(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'DefaultConnection' is not configured.");

        hostBuilder.UseWolverine(options =>
        {
            // Takes the connection string *name*; Wolverine resolves it from IConfiguration itself.
            options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                .AutoProvision()
                .UseConventionalRouting();
            
            options.PersistMessagesWithPostgresql(connectionString);
            options.UseEntityFrameworkCoreTransactions();
            
            options.Policies.UseDurableLocalQueues();

            // UseDurableLocalQueues covers in-process queues only, so without these the Postgres
            // message store above is configured and unused for anything arriving from RabbitMQ.
            // The inbox is what the six consumers already assume: persisted before handling, so a
            // crash mid-handler redelivers instead of losing, and a redelivery is deduplicated.
            options.Policies.UseDurableInboxOnAllListeners();

            // Bookings publishes nothing today. Enrolled anyway so the first thing that does is
            // durable by default rather than by remembering.
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        });
    }
}