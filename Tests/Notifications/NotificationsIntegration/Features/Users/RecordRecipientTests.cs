using Microsoft.EntityFrameworkCore;
using Notifications.Features.Users;
using NotificationsIntegration.Fixtures;
using TicketMaster.Common.IntegrationEvents;

namespace NotificationsIntegration.Features.Users;

public sealed class RecordRecipientTests(NotificationsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task A_registered_user_is_remembered_by_their_address()
    {
        var message = new UserRegisteredIntegrationEvent(Guid.NewGuid(), "buyer@example.com");

        await HandleAsync<UserRegisteredHandler>((handler, context) => handler.Handle(message, context, default));

        var recipient = await ReadAsync(c => c.Recipients.SingleAsync(r => r.UserId == message.UserId));
        Assert.Equal("buyer@example.com", recipient.Email);
    }

    [Fact]
    public async Task A_redelivered_registration_adds_nothing()
    {
        var message = new UserRegisteredIntegrationEvent(Guid.NewGuid(), "buyer@example.com");

        await HandleAsync<UserRegisteredHandler>((handler, context) => handler.Handle(message, context, default));
        await HandleAsync<UserRegisteredHandler>((handler, context) => handler.Handle(message, context, default));

        Assert.Equal(1, await ReadAsync(c => c.Recipients.CountAsync(r => r.UserId == message.UserId)));
    }
}
