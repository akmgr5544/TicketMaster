using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Data;
using NotificationsIntegration.Fixtures;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;

namespace NotificationsIntegration.Mechanics;

// What only the running host can show: messages crossing RabbitMQ to Notifications without being taken from
// PaymentSystem, the transaction and outbox around a handler, and an email actually reaching a mail server.
[Collection(NotificationsHostCollection.Name)]
public sealed class NotificationsHostTests(NotificationsHostFixture fixture)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    // Two services on one queue compete for its messages; on their own queues bound to the same exchange, each gets
    // a copy. If Notifications listened on the default queue, about half of these would never reach PaymentSystem.
    [Fact]
    public async Task A_message_both_services_consume_reaches_each_of_them()
    {
        var userIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();

        foreach (var userId in userIds)
            await fixture.Publisher.PublishAsync(new UserRegisteredIntegrationEvent(userId, $"{userId:N}@example.com"));

        await Eventually(() => Task.FromResult(userIds.All(fixture.ReceivedByPayments.Has)), "PaymentSystem's stand-in did not get every message");
        await Eventually(async () => await RecipientsKnownAsync(userIds) == userIds.Length, "Notifications did not get every message");
    }

    [Fact]
    public async Task A_paid_booking_ends_as_an_email_to_its_buyer()
    {
        var bookingId = Random.Shared.NextInt64(1, long.MaxValue);
        var buyerId = Guid.NewGuid();
        var email = $"{buyerId:N}@example.com";

        await fixture.Publisher.PublishAsync(new UserRegisteredIntegrationEvent(buyerId, email));
        await fixture.Publisher.PublishAsync(new PaymentRequestedIntegrationEvent(bookingId, buyerId, Guid.NewGuid(), 40m, "USD"));
        await Eventually(async () => await RecipientsKnownAsync([buyerId]) == 1 && await BuyerKnownAsync(bookingId));

        await fixture.Publisher.PublishAsync(new BookingPaidIntegrationEvent(bookingId));

        await Eventually(async () => (await fixture.EmailsToAsync(email)).Any(message => message.Subject.Contains(bookingId.ToString())));
    }

    [Fact]
    public void Every_application_endpoint_is_durable()
    {
        var endpoints = fixture.Services.GetRequiredService<IWolverineRuntime>().Options.Transports
            .AllEndpoints()
            .Where(endpoint => endpoint.Role == EndpointRole.Application)
            // What carries this service's messages: its listeners and its local queues. The exchanges are declared
            // only so its queues can be bound to them; nothing is ever sent to one.
            .Where(endpoint => endpoint.IsListener || endpoint.Uri.Scheme == "local")
            .ToArray();

        Assert.NotEmpty(endpoints);
        Assert.Empty(endpoints.Where(endpoint => endpoint.Mode != EndpointMode.Durable).Select(endpoint => endpoint.Uri.ToString()));
    }

    private async Task<int> RecipientsKnownAsync(Guid[] userIds)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Recipients
            .CountAsync(r => userIds.Contains(r.UserId));
    }

    private async Task<bool> BuyerKnownAsync(long bookingId)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().BookingBuyers
            .AnyAsync(b => b.BookingId == bookingId);
    }

    private static async Task Eventually(Func<Task<bool>> condition, string because = "The condition did not hold in time.")
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(200);
        }

        throw new TimeoutException(because);
    }
}
