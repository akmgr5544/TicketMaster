using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Features.Users;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentIntegration.Features.Users;

public sealed class RecordUserTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task A_registered_user_is_kept_by_the_id_and_email_Users_announced()
    {
        var message = new UserRegisteredIntegrationEvent(Guid.NewGuid(), "buyer@example.com");

        await HandleAsync<UserRegisteredHandler>((handler, context, _) => handler.Handle(message, context, default));

        var user = await ReadAsync(c => c.Users.SingleAsync(u => u.UserId == message.UserId));
        Assert.Equal("buyer@example.com", user.Email);
    }

    [Fact]
    public async Task A_redelivered_registration_adds_no_second_user()
    {
        var message = new UserRegisteredIntegrationEvent(Guid.NewGuid(), "buyer@example.com");

        await HandleAsync<UserRegisteredHandler>((handler, context, _) => handler.Handle(message, context, default));
        await HandleAsync<UserRegisteredHandler>((handler, context, _) => handler.Handle(message, context, default));

        Assert.Equal(1, await ReadAsync(c => c.Users.CountAsync(u => u.UserId == message.UserId)));
    }
}
