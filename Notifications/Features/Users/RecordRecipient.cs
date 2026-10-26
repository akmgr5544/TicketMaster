using Microsoft.EntityFrameworkCore;
using Notifications.Data;
using Notifications.Domain;
using TicketMaster.Common.IntegrationEvents;

namespace Notifications.Features.Users;

// Remembers a user's address. A redelivery adds nothing.
public sealed class UserRegisteredHandler
{
    public async Task Handle(UserRegisteredIntegrationEvent message, NotificationsDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Recipients.AnyAsync(r => r.UserId == message.UserId, cancellationToken))
            return;

        context.Recipients.Add(new Recipient(message.UserId, message.Email));
    }
}
