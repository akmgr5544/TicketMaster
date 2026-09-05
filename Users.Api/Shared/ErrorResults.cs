namespace Users.Api.Shared;

public static class ErrorResults
{
    /// <summary>
    /// The one place <see cref="ErrorType"/> is read. Endpoints returning <c>Results.BadRequest</c>
    /// directly made every failure a 400 and the enum decorative.
    /// </summary>
    public static IResult ToProblem(this Error error)
    {
        var status = StatusFor(error.Type);

        return Results.Problem(
            title: TitleFor(error.Type),
            // Call sites put the human-readable text in Code and leave Message empty as often as not.
            detail: string.IsNullOrWhiteSpace(error.Message) ? error.Code : error.Message,
            statusCode: status,
            type: $"https://httpstatuses.io/{status}",
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.BadRequest => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.NotFound => "Not found",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.BadRequest => "Invalid request",
        _ => "Invalid request"
    };
}
