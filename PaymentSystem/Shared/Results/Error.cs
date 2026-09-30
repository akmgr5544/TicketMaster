namespace PaymentSystem.Shared.Results;

public sealed record Error(string Code, ErrorType Type, string Message)
{
    public static Error NotFound(string code, string message) => new(code, ErrorType.NotFound, message);
    public static Error BadRequest(string code, string message) => new(code, ErrorType.BadRequest, message);
    public static Error Conflict(string code, string message) => new(code, ErrorType.Conflict, message);
}
