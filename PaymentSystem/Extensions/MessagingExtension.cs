using Microsoft.EntityFrameworkCore;
using JasperFx.Core;
using PaymentSystem.Data;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using Wolverine.Runtime;

namespace PaymentSystem.Extensions;

public static class MessagingExtension
{
    // Kept apart from AddInfrastructureServices so a plain ServiceProvider can host the slices without
    // Wolverine; everything that needs a running Wolverine is registered here.
    public static void ConfigureMessaging(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'DefaultConnection' is not configured.");

        hostBuilder.UseWolverine(options =>
        {
            // Pinned rather than inferred: under a test host the entry assembly is the test runner, and the
            // consumers would silently go undiscovered.
            options.ApplicationAssembly = typeof(MessagingExtension).Assembly;

            // Takes the connection string *name*; Wolverine resolves it from IConfiguration itself. Only the
            // shared contracts cross the broker; the expiry timer is this service's own business.
            options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                .AutoProvision()
                .UseConventionalRouting(conventions => conventions.IncludeTypes(type =>
                    type.Namespace == typeof(PaymentRequestedIntegrationEvent).Namespace));
            options.PublishMessage<CheckoutExpiryDue>().ToLocalQueue("checkout-expiry");

            // The expiry and cancellation handlers retry a lost race once themselves; a second loss is retried
            // later from a fresh scope, where the change tracker holds nothing stale.
            options.OnException<DbUpdateConcurrencyException>()
                .ScheduleRetry(1.Seconds(), 5.Seconds(), 30.Seconds());

            options.PersistMessagesWithPostgresql(connectionString);
            options.UseEntityFrameworkCoreTransactions();

            // PersistMessagesWithPostgresql only creates the storage; these enrol the endpoints in it.
            // Without the inbox, a crash mid-handler loses a PaymentRequested; without the outbox,
            // BookingPaid would be sent from memory and the message store would sit unused; without durable local
            // queues, a checkout's expiry timer would be lost on restart.
            options.Policies.UseDurableLocalQueues();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();

            options.Services.AddScoped<OutboxFlushInterceptor>();
            options.Services.ConfigureDbContext<PaymentDbContext>((serviceProvider, dbContextOptions) =>
                dbContextOptions.AddInterceptors(serviceProvider.GetRequiredService<OutboxFlushInterceptor>()));
            // A factory, not a type mapping: Wolverine then resolves it from the message's own scope, the
            // one MediatR's handlers and their DbContext come from, instead of building a second copy inline.
            options.Services.AddScoped<IIntegrationEventPublisher>(serviceProvider =>
                new OutboxIntegrationEventPublisher(
                    serviceProvider.GetRequiredService<IWolverineRuntime>(),
                    serviceProvider.GetRequiredService<PaymentDbContext>(),
                    serviceProvider.GetRequiredService<OutboxFlushInterceptor>()));
        });
    }
}
