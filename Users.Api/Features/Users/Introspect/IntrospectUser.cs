using System.Security.Claims;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Users.Api.Database;
using Users.Api.Shared;

namespace Users.Api.Features.Users.Introspect;

public static class IntrospectUser
{
    public sealed record Query(Guid UserId) : IRequest<Result<Response>>;

    public sealed record Response(
        string Id,
        string Email,
        string FirstName,
        string LastName,
        string UserName,
        string Role,
        IReadOnlyCollection<string> Permissions);

    internal sealed class Handler : IRequestHandler<Query, Result<Response>>
    {
        private readonly UsersDomainContext _dbContext;

        public Handler(UsersDomainContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            var user = await _dbContext.Users
                .Where(x => x.Id == request.UserId)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.FirstName,
                    x.LastName,
                    x.UserName,
                    x.Role
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                // The token verified but its subject is gone — a deleted user holding a live token.
                var error = new Error("user_not_found", ErrorType.Unauthorized, "User not found");
                return Result<Response>.Failure(error);
            }

            // Permissions are part of the agreed contract but no permission model exists yet.
            // Returning an empty collection keeps the wire shape stable for when one is added.
            var result = new Response(
                // Response.Id is the wire contract the gateway reads back as a string.
                user.Id.ToString(),
                user.Email,
                user.FirstName,
                user.LastName,
                user.UserName,
                user.Role.ToString(),
                []);

            return Result<Response>.Success(result);
        }
    }
}

public sealed class IntrospectEndpoints : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("api/users/auth", async (ClaimsPrincipal principal, ISender sender) =>
        {
            // The token itself is validated by the JwtBearer handler before reaching here. The subject
            // is the user's GUID in string form; reject anything that is not a parseable GUID.
            var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(subject, out var userId))
                return Results.Unauthorized();

            var result = await sender.Send(new IntrospectUser.Query(userId));
            if (!result.IsSuccess)
                return Results.Unauthorized();

            return Results.Ok(result.Value);
        })
        .RequireAuthorization();
    }
}
