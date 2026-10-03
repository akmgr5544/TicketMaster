using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Users.Api.Database;
using Users.Api.Entities;

namespace Users.Api.Features.Users;

// Checks the role the store holds now, not the one baked into the token at login: a token lives a day, so
// a role claim would let a demoted admin keep admin access that long and make a promoted user log in again.
public sealed record StoredRoleRequirement(UserRole Role) : IAuthorizationRequirement;

internal sealed class StoredRoleHandler : AuthorizationHandler<StoredRoleRequirement>
{
    private readonly UsersDomainContext _dbContext;

    public StoredRoleHandler(UsersDomainContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, StoredRoleRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return;

        var holdsRole = await _dbContext.Users
            .AnyAsync(u => u.Id == userId && u.Role == requirement.Role);

        if (holdsRole)
            context.Succeed(requirement);
    }
}
