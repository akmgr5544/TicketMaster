using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Users.Api.Shared;

namespace UsersApi;

/// <summary>
/// The refresh endpoint collapses not-found, expired and invalid into one uniform 401 so it cannot be
/// used as an oracle for which tokens or users exist. This pins the shape of that single failure.
/// </summary>
public class RefreshFailureTests
{
    private static readonly Error UniformFailure =
        new("invalid_refresh_token", ErrorType.Unauthorized, "Invalid or expired refresh token");

    [Fact]
    public void Uniform_failure_is_401()
    {
        var status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(UniformFailure.ToProblem()).StatusCode;
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public void Uniform_failure_carries_the_sentence_as_detail_and_the_code_extension()
    {
        var problem = Assert.IsType<ProblemHttpResult>(UniformFailure.ToProblem());

        Assert.Equal("Invalid or expired refresh token", problem.ProblemDetails.Detail);
        Assert.Equal("invalid_refresh_token", Assert.Contains("code", problem.ProblemDetails.Extensions));
    }
}
