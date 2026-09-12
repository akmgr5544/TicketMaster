using Events.Application.IntegrationEvents;
using Events.Application.Pipelines;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.CosmosDb;
using Wolverine.RabbitMQ;

namespace Events.Application.Extensions;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {

        services.AddMediatR(cf =>
            cf.RegisterServicesFromAssembly(typeof(ServiceCollectionExtension).Assembly));

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ConcurrencyRetryBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();

        return services;
    }

    public static void ConfigureRabbitMq(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        var databaseName = configuration["CosmosConfigs:Database"]
            ?? throw new InvalidOperationException("CosmosConfigs:Database is not configured.");

        hostBuilder.UseWolverine(options =>
        {
            // Takes the connection string *name*; Wolverine resolves it from IConfiguration itself.
            options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                .AutoProvision()
                .UseConventionalRouting();

            options.Policies.DisableConventionalLocalRouting();

            // Durable Cosmos outbox: envelopes persist and are relayed after a crash. Not atomic with
            // the aggregate write (a separate container, per-item upsert) — an accepted small window.
            options.UseCosmosDbPersistence(databaseName);
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        });
    }
}