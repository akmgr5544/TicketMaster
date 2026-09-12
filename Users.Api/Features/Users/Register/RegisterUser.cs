using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
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
    private const int MinPasswordLength = 8;

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
            if (Validate(request) is { } validationError)
                return Result<Response>.Failure(validationError);

            if (request.Password != request.ConfirmPassword)
            {
                var error = new Error("passwords_do_not_match", ErrorType.BadRequest, "Passwords do not match");
                return Result<Response>.Failure(error);
            }

            // Fast path for the friendly message. The unique indexes on Email/UserName are the real
            // guard against a concurrent duplicate — see the DbUpdateException catch below.
            var dbUser = await _dbContext.Users.FirstOrDefaultAsync(x =>
                x.Email == request.Email ||
                x.UserName == request.UserName, cancellationToken);

            if (dbUser != null)
                return Result<Response>.Failure(InUseError());

            // Bootstrap: the first account ever registered is the admin; everyone after is a customer.
            // Admins grant the role to others via the admin-only set-role endpoint.
            var isFirstUser = !await _dbContext.Users.AnyAsync(cancellationToken);

            var user = new User(request.UserName,
                request.Email,
                request.FirstName,
                request.LastName,
                request.PhoneNumber)
            {
                Role = isFirstUser ? UserRole.Admin : UserRole.Customer
            };

            var hashedPass = _passwordHasher.HashPassword(user, request.Password);
            user.PasswordHash = hashedPass;

            var token = TokenService.CreateToken(user, _authOptions);
            var refreshToken = TokenService.CreateRefreshToken(_authOptions);

            user.RefreshToken = refreshToken.Hash;
            user.RefreshTokenExpires = refreshToken.Expires;

            _dbContext.Users.Add(user);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Lost the check-then-insert race against a concurrent registration; the unique index
                // rejected the duplicate. Map to the same 400 the pre-check would have returned.
                return Result<Response>.Failure(InUseError());
            }

            var result = new Response(token, refreshToken.Raw);
            return Result<Response>.Success(result);
        }

        private static Error InUseError() =>
            new("email_or_username_in_use", ErrorType.BadRequest, "Email or UserName already in use");

        private static Error? Validate(Command request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || !MailAddress.TryCreate(request.Email, out _))
                return new Error("invalid_email", ErrorType.BadRequest, "A valid email is required");

            if (string.IsNullOrWhiteSpace(request.UserName))
                return new Error("invalid_username", ErrorType.BadRequest, "A user name is required");

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < MinPasswordLength)
                return new Error("invalid_password", ErrorType.BadRequest,
                    $"A password of at least {MinPasswordLength} characters is required");

            return null;
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
                return result.Error!.ToProblem();

            return Results.Ok(result.Value);
        });
    }
}