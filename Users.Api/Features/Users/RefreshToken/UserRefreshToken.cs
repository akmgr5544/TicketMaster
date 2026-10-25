using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Users.Api.Database;
using Users.Api.Options;
using Users.Api.Shared;

namespace Users.Api.Features.Users.RefreshToken;

public static class UserRefreshToken
{
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

            if (user == null || user.RefreshTokenExpires <= DateTime.UtcNow)
            {
                var error = new Error("invalid_refresh_token", ErrorType.Unauthorized,
                    "Invalid or expired refresh token");
                return Result<Response>.Failure(error);
            }

            var token = TokenService.CreateToken(user, _authOptions);
            var refreshToken = TokenService.CreateRefreshToken(_authOptions);

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