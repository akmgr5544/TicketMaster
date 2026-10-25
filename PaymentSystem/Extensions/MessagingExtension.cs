using JasperFx.Core;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Exceptions;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Messaging;
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

            // This service's own steps stay in process, on queues stored in Postgres: a scheduled expiry or a refund
            // waiting for the PSP survives a restart.
            options.PublishMessage<CheckoutExpiryDue>().ToLocalQueue("checkout-expiry");
            options.PublishMessage<ProcessRefund>().ToLocalQueue("refunds");
            options.PublishMessage<RecordRefundOutcome>().ToLocalQueue("refunds");

            // A handler that uses PaymentDbContext runs in one transaction: Wolverine saves its changes and the
            // messages it returns together, and sends those messages only after the commit.
            options.PersistMessagesWithPostgresql(connectionString);
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();

            // Two copies of one message racing to insert lose on a unique index (a checkout, a booking claim), and two
            // writers on one checkout or wallet lose on its version; the retry starts from a fresh scope and decides
            // over the winner's state.
            options.OnException<DbUpdateException>()
                .RetryWithCooldown(100.Milliseconds(), 1.Seconds(), 5.Seconds());

            // A PSP that is down or rate-limiting is asked again, with room to recover. A repeated refund is answered
            // by the PSP with the first one, since the request id is its idempotency key.
            options.OnException<PaymentProviderException>(exception => exception.Kind == PaymentProviderErrorKind.Transient)
                .ScheduleRetry(10.Seconds(), 1.Minutes(), 5.Minutes(), 30.Minutes());

            // Here rather than in AddInfrastructureServices: it records outcomes through Wolverine's bus.
            options.Services.AddHostedService<ReconcileOrdersJob>();
        });
    }
}
