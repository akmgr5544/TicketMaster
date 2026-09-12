using MediatR;
using Microsoft.EntityFrameworkCore;
using Users.Api.Database;
using Users.Api.Entities;
using Users.Api.Shared;

namespace Users.Api.Features.Users.SetRole;

public static class SetUserRole
{
    public sealed record Command(Guid UserId, string Role) : IRequest<Result>;

    internal sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly UsersDomainContext _dbContext;

        public Handler(UsersDomainContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
                return new Error("invalid_role", ErrorType.BadRequest, $"Unknown role '{request.Role}'");

            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == request.UserId, cancellationToken);
            if (user is null)
                return new Error("user_not_found", ErrorType.NotFound, "User not found");

            user.Role = role;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }
}

public sealed class SetUserRoleEndpoint : IEndpointMarker
{
    public sealed record Request(string Role);

    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("api/users/{id:guid}/role", async (Guid id, Request request, ISender sender) =>
        {
            var result = await sender.Send(new SetUserRole.Command(id, request.Role));
            return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
        })
        .RequireAuthorization("AdminOnly");
    }
}
