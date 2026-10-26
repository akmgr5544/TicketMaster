using JasperFx.Core;
using Microsoft.EntityFrameworkCore;
using Notifications.Email;
using Notifications.Features.Notifications;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace Notifications.Extensions;

public static class MessagingExtension
{
    // The messages this service reacts to. Each is published to a fanout exchange named after its type.
    public static readonly Type[] Consumed =
    [
        typeof(PaymentRequestedIntegrationEvent),
        typeof(UserRegisteredIntegrationEvent),
        typeof(BookingPaidIntegrationEvent),
        typeof(BookingPaymentFailedIntegrationEvent),
        typeof(BookingCancelledIntegrationEvent),
        typeof(BookingRefundedIntegrationEvent)
    ];

    // Its own queue for each, never the default one named after the type: PaymentSystem and Bookings already listen on those, and
    // two services on one queue compete for its messages instead of each getting a copy.
    public static string QueueFor(Type messageType) => $"notifications.{messageType.Name}";

    public static void ConfigureMessaging(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("NotificationsDatabase")
                               ?? throw new InvalidOperationException("Connection string 'NotificationsDatabase' is not configured.");

        hostBuilder.UseWolverine(options =>
        {
            // Pinned rather than inferred: under a test host the entry assembly is the test runner.
            options.ApplicationAssembly = typeof(MessagingExtension).Assembly;

            // Declared explicitly rather than by conventional routing, which would pick the default queue names.
            var rabbit = options.UseRabbitMqUsingNamedConnection("RabbitMQ").AutoProvision();
            foreach (var messageType in Consumed)
            {
                var queue = QueueFor(messageType);
                // The exchange is the one each publisher sends to, named after the message type, fanout.
                rabbit.BindExchange(messageType.FullName!, ExchangeType.Fanout).ToQueue(queue);
                options.ListenToRabbitQueue(queue);
            }

            options.PublishMessage<SendEmail>().ToLocalQueue("emails");

            // A handler that uses the DbContext runs in one transaction with the messages it returns.
            options.PersistMessagesWithPostgresql(connectionString);
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();

            // Two copies of one message racing to insert lose on a unique index; the retry finds the winner's row.
            options.OnException<DbUpdateException>()
                .RetryWithCooldown(100.Milliseconds(), 1.Seconds(), 5.Seconds());

            // A mail server that is down is tried again, with room to recover.
            options.OnException<EmailDeliveryException>()
                .ScheduleRetry(10.Seconds(), 1.Minutes(), 5.Minutes(), 30.Minutes());
        });
    }
}
