using System.Diagnostics.CodeAnalysis;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Users.Api.Database;
using Users.Api.Entities;
using Users.Api.Options;
using Users.Api.Shared;

namespace Users.Api.Features.Users.Authenticate;

public static class AuthenticateUser
{
    public sealed record Command(string UserName, string Password) : IRequest<Result<Response>>;

    public sealed record Response(string Token, string RefreshToken);

    internal sealed class Handler : IRequestHandler<Command, Result<Response>>
    {
        private readonly UsersDomainContext _dbContext;
        private readonly PasswordHasher<User> _passwordHasher;
        private readonly AuthOptions _authOptions;

        public Handler(UsersDomainContext dbContext,
            IOptions<AuthOptions> authOptions)
        {
            _dbContext = dbContext;
            _passwordHasher = new();
            _authOptions = authOptions.Value;
        }

        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.UserName == request.UserName,
                cancellationToken);
            if (user == null)
                return Result<Response>.Failure(
                    new Error("invalid_credentials", ErrorType.Unauthorized, "Invalid credentials"));

            var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verification == PasswordVerificationResult.Failed)
            {
                return Result<Response>.Failure(
                    new Error("invalid_credentials", ErrorType.Unauthorized, "Invalid credentials"));
            }

            // The stored hash uses outdated parameters; upgrade it in place. Login still succeeds.
            if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            }

            var token = TokenService.CreateToken(user, _authOptions);
            var refreshToken = TokenService.CreateRefreshToken(_authOptions);

            // user is tracked, so mutating it is enough — SaveChangesAsync persists only the changed
            // columns. No Update call (that would rewrite every column).
            user.RefreshToken = refreshToken.Hash;
            user.RefreshTokenExpires = refreshToken.Expires;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var result = new Response(token, refreshToken.Raw);

            return Result<Response>.Success(result);
        }
    }
}

public sealed class LoginEndpoint : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/users/login", async (AuthenticateUser.Command request, ISender sender) =>
        {
            var result = await sender.Send(request);
            if (!result.IsSuccess)
                return result.Error!.ToProblem();

            return Results.Ok(result.Value);
        });
    }
}