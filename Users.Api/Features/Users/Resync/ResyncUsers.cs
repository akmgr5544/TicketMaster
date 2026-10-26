using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketMaster.Common.IntegrationEvents;
using Users.Api.Database;
using Users.Api.Shared;
using Wolverine.EntityFrameworkCore;

namespace Users.Api.Features.Users.Resync;

// Announces every account again, for the services that keep a copy and missed the ones registered before they
// subscribed. Every consumer skips a user it already has, so running it twice is harmless.
public static class ResyncUsers
{
    public sealed record Command : IRequest<Result<Response>>;

    public sealed record Response(int Published);

    internal sealed class Handler : IRequestHandler<Command, Result<Response>>
    {
        private readonly IDbContextOutbox<UsersDomainContext> _outbox;

        public Handler(IDbContextOutbox<UsersDomainContext> outbox)
        {
            _outbox = outbox;
        }

        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var users = await _outbox.DbContext.Users
                .AsNoTracking()
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(cancellationToken);

            foreach (var user in users)
                await _outbox.PublishAsync(new UserRegisteredIntegrationEvent(user.Id, user.Email));

            await _outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

            return Result<Response>.Success(new Response(users.Count));
        }
    }
}

public sealed class ResyncUsersEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/users/resync", async (ISender sender) =>
        {
            var result = await sender.Send(new ResyncUsers.Command());
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
        })
        .RequireAuthorization("AdminOnly");
    }
}
