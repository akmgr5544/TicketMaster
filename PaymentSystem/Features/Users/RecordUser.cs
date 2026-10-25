using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.Users;

// Users registered an account: keep Payments' own copy of it. A redelivery finds the user already there and adds nothing.
public sealed class UserRegisteredHandler
{
    public async Task Handle(UserRegisteredIntegrationEvent message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Users.AnyAsync(u => u.UserId == message.UserId, cancellationToken))
            return;

        context.Users.Add(User.Create(message.UserId, message.Email));
    }
}
