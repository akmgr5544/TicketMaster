using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace Users.Api.Extensions;

public static class MessagingExtension
{
    // Users only publishes. The message store lives in the users database, beside the rows it describes, so a
    // registration and its UserRegistered commit together and the message survives a crash before it is sent.
    public static void ConfigureMessaging(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UsersDatabase")
                               ?? throw new InvalidOperationException("Connection string 'UsersDatabase' is not configured.");

        hostBuilder.UseWolverine(options =>
        {
            // Pinned rather than inferred: under a test host the entry assembly is the test runner.
            options.ApplicationAssembly = typeof(MessagingExtension).Assembly;

            options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                .AutoProvision()
                .UseConventionalRouting(conventions => conventions.IncludeTypes(type =>
                    type.Namespace == typeof(UserRegisteredIntegrationEvent).Namespace));
            options.Policies.DisableConventionalLocalRouting();

            options.PersistMessagesWithPostgresql(connectionString);
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
        });
    }
}
