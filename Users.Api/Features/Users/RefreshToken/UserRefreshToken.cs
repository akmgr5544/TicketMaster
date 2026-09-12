using System.Diagnostics.CodeAnalysis;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Users.Api.Database;
using Users.Api.Options;
using Users.Api.Shared;

namespace Users.Api.Features.Users.RefreshToken;

public static class UserRefreshToken
{
    // Identity comes from the presented refresh token, never a user id in the body — a body-supplied
    // id lets a caller nominate whose session to act on and turns the endpoint into an oracle for
    // which ids exist.
    public sealed record Command(string RefreshToken) : IRequest<Result<Response>>;

    public sealed record Response(string Token, string RefreshToken);

    internal sealed class Handler : IRequestHandler<Command, Result<Response>>
    {
        private readonly UsersDomainContext _dbContext;
        private readonly AuthOptions _authOptions;

        public Handler(UsersDomainContext dbContext,
            IOptions<AuthOptions> authOptions)
        {
            _dbContext = dbContext;
            _authOptions = authOptions.Value;
        }

        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var tokenHash = TokenService.HashRefreshToken(request.RefreshToken);
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.RefreshToken == tokenHash,
                cancellationToken);

            // Not found, expired, or otherwise invalid all collapse to one uniform failure so the
            // endpoint reveals nothing about which tokens or users exist.
            if (user == null || user.RefreshTokenExpires <= DateTime.UtcNow)
            {
                var error = new Error("invalid_refresh_token", ErrorType.Unauthorized,
                    "Invalid or expired refresh token");
                return Result<Response>.Failure(error);
            }

            var token = TokenService.CreateToken(user, _authOptions);
            var refreshToken = TokenService.CreateRefreshToken(_authOptions);

            // Rotate: a fresh access token and a fresh refresh token (new hash + expiry). user is
            // tracked, so no Update call is needed.
            user.RefreshToken = refreshToken.Hash;
            user.RefreshTokenExpires = refreshToken.Expires;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var result = new Response(token, refreshToken.Raw);
            return Result<Response>.Success(result);
        }
    }
}

public sealed class RefreshTokenEndpoints : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/users/refreshToken", async (UserRefreshToken.Command request, ISender sender) =>
        {
            var result = await sender.Send(request);
            if (!result.IsSuccess)
                return result.Error!.ToProblem();

            return Results.Ok(result.Value);
        });
    }
}