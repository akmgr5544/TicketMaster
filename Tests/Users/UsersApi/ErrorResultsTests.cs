using Microsoft.AspNetCore.Http;
using Users.Api.Shared;

namespace UsersApi;

/// <summary>
/// Until this mapping existed every endpoint answered 400, whatever the error said, so the four
/// <see cref="ErrorType"/> values were indistinguishable to a caller.
/// </summary>
public class ErrorResultsTests
{
    private static int StatusOf(Error error) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(error.ToProblem()).StatusCode
        ?? throw new InvalidOperationException("The result carried no status code.");

    [Theory]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.BadRequest, StatusCodes.Status400BadRequest)]
    public void Each_error_type_maps_to_its_own_status(ErrorType type, int expected)
    {
        Assert.Equal(expected, StatusOf(new Error("some_code", type, "message")));
    }

    /// <summary>
    /// Call sites pass the human-readable text as <c>Code</c> and leave <c>Message</c> empty, so a
    /// detail taken straight from <c>Message</c> would be blank on most real failures.
    /// </summary>
    [Fact]
    public void Falls_back_to_the_code_when_there_is_no_message()
    {
        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(
            new Error("Invalid refresh token", ErrorType.Unauthorized, "").ToProblem());

        Assert.Equal("Invalid refresh token", problem.ProblemDetails.Detail);
    }

    [Fact]
    public void Carries_the_code_so_a_client_can_branch_on_it()
    {
        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(
            new Error("user_not_found", ErrorType.NotFound, "No such user").ToProblem());

        Assert.Equal("user_not_found", Assert.Contains("code", problem.ProblemDetails.Extensions));
    }
}
