namespace PaymentSystem.Shared.Results;

public static class ErrorResults
{
    // The one place ErrorType is read, so the status an error maps to cannot drift between endpoints.
    public static IResult ToProblem(this Error error)
    {
        var status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        // Qualified: inside this namespace "Results" names the folder, not ASP.NET's Results class.
        return Microsoft.AspNetCore.Http.Results.Problem(
            title: error.Type.ToString(),
            detail: error.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
