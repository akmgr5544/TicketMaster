using System.Diagnostics.CodeAnalysis;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Users.Api.Database;
using Users.Api.Entities;
using Users.Api.Options;
using Users.Api.Shared;

namespace Users.Api.Features.Users.Register;

public static class RegisterUser
{
    public sealed record Command(
        string UserName,
        string Email,
        string Password,
        string ConfirmPassword,
        string FirstName,
        string LastName,
        string PhoneNumber) : IRequest<Result<Response>>;

    public sealed record Response(string Token, string RefreshToken);


    internal sealed class Handler : IRequestHandler<Command, Result<Response>>
    {
        private readonly AuthOptions _authOptions;
        private readonly UsersDomainContext _dbContext;
        private readonly PasswordHasher<User> _passwordHasher;

        public Handler(UsersDomainContext dbContext,
            IOptions<AuthOptions> authOptions)
        {
            _authOptions = authOptions.Value;
            _dbContext = dbContext;
            _passwordHasher = new();
        }

        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.Password != request.ConfirmPassword)
            {
                var error = new Error("Passwords do not match", ErrorType.BadRequest, "");
                return Result<Response>.Failure(error);
            }

            var dbUser = await _dbContext.Users.FirstOrDefaultAsync(x =>
                x.Email == request.Email ||
                x.UserName == request.UserName, cancellationToken);

            if (dbUser != null)
            {
                var error = new Error("Email or UserName already in use", ErrorType.BadRequest, "");
                return Result<Response>.Failure(error);
            }

            var user = new User(request.UserName,
                request.Email,
                request.FirstName,
                request.LastName,
                request.PhoneNumber);

            var hashedPass = _passwordHasher.HashPassword(user, request.Password);
            user.PasswordHash = hashedPass;

            var token = TokenService.CreateToken(user, _authOptions);
            var refreshToken = TokenService.CreateRefreshToken(user, _authOptions);

            user.RefreshToken = refreshToken;

            await _dbContext.Users.AddAsync(user, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var result = new Response(token, refreshToken);
            return Result<Response>.Success(result);
        }
    }
}

public sealed class RegistrationEndpoints : IEndpointMarker
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/users/registration", async (RegisterUser.Command request, ISender sender) =>
        {
            var result = await sender.Send(request);
            if (!result.IsSuccess)
                return Results.BadRequest(result.Error);

            return Results.Ok(result.Value);
        });
    }
}