namespace Users.Api.Shared;

public record Error(string Code, ErrorType Type, string Message);