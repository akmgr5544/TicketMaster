using MediatR;
using Microsoft.EntityFrameworkCore;
using Users.Api.Database;
using Users.Api.Entities;
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
        string PhoneNumber) : IRequest<Result>;

    internal sealed class Handler : IRequestHandler<Command, Result>
    {
        private readonly UsersDomainContext _dbContext;

        public Handler(UsersDomainContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.Password != request.ConfirmPassword)
            {
                return Result.Failure(new Error("Passwords do not match", ErrorType.BadRequest, ""));
            }

            var dbUser = await _dbContext.Users.FirstOrDefaultAsync(x =>
                x.Email == request.Email ||
                x.UserName == request.UserName, cancellationToken);

            if (dbUser != null)
            {
                return Result.Failure(new Error("Email or UserName already in use", ErrorType.BadRequest, ""));
            }

            var user = new User(request.UserName,
                request.Email,
                request.Password,
                request.FirstName,
                request.LastName,
                request.PhoneNumber);

            await _dbContext.Users.AddAsync(user, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
    }

    public static void MapEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("api/users", async (Command request, ISender sender) =>
        {
            await sender.Send(request);
            return Results.Ok();
        });
    }
}