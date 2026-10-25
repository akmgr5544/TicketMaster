using JasperFx.Core;
using Microsoft.EntityFrameworkCore;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace PaymentSystem.Extensions;

public static class MessagingExtension
{
    public static void ConfigureMessaging(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        hostBuilder.UseWolverine(options =>
        {
            // Pinned rather than inferred: under a test host the entry assembly is the test runner, and the
            // handlers would silently go undiscovered.
            options.ApplicationAssembly = typeof(MessagingExtension).Assembly;

            // Only the shared contracts cross the broker, routed by type the same way Bookings publishes them.
            options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                .AutoProvision()
                .UseConventionalRouting(conventions => conventions.IncludeTypes(type =>
                    type.Namespace == typeof(PaymentRequestedIntegrationEvent).Namespace));

            // A handler that uses PaymentDbContext runs in one transaction: Wolverine saves its changes and the
            // messages it returns together, and sends those messages only after the commit.
            options.PersistMessagesWithPostgresql(connectionString);
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();

            // Two copies of one message racing to insert lose on a unique index; the retry finds the winner's row.
            options.OnException<DbUpdateException>()
                .RetryWithCooldown(100.Milliseconds(), 1.Seconds(), 5.Seconds());
        });
    }
}
