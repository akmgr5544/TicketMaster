using System.Security.Claims;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Users.Api.Database;
using Users.Api.Shared;

namespace Users.Api.Features.Users.Introspect;

/// <summary>
/// Token introspection for the API gateway. The gateway calls this on every authenticated request
/// and materializes the response into claims, so the response shape is a public contract.
/// </summary>
public static class IntrospectUser
{
    public sealed record Query(long UserId) : IRequest<Result<Response>>;

    public sealed record Response(
        string Id,
        string Email,
        string FirstName,
        string LastName,
        string UserName,
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
                    x.UserName
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
                user.Id.ToString(),
                user.Email,
                user.FirstName,
                user.LastName,
                user.UserName,
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
            // The token itself is validated by the JwtBearer handler before reaching here.
            var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(subject, out var userId))
                return Results.Unauthorized();

            var result = await sender.Send(new IntrospectUser.Query(userId));
            if (!result.IsSuccess)
                return Results.Unauthorized();

            return Results.Ok(result.Value);
        })
        .RequireAuthorization();
    }
}
