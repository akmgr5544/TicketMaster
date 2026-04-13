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
    public record Command(long UserId, string RefreshToken) : IRequest<Result<Response>>;

    public sealed record Response(string Token, string RefreshToken);

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
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
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == request.UserId, cancellationToken);

            if (user == null)
            {
                var error = new Error("User not found", ErrorType.BadRequest, "");
                return Result<Response>.Failure(error);
            }

            if (user.RefreshToken != request.RefreshToken || user.RefreshTokenExpires <= DateTime.UtcNow)
            {
                var error = new Error("Invalid refresh token", ErrorType.BadRequest, "");
                return Result<Response>.Failure(error);
            }

            var token = TokenService.CreateToken(user, _authOptions);
            var refreshToken = TokenService.CreateRefreshToken(user, _authOptions);

            user.RefreshToken = refreshToken;
            _dbContext.Users.Update(user);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var result = new Response(token, refreshToken);
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
                return Results.BadRequest(result.Error);

            return Results.Ok(result.Value);
        });
    }
}