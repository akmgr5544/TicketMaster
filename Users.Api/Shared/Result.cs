namespace Users.Api.Shared;

public record Result()
{
    public bool IsSuccess { get; init; }
    public Error? Error { get; init; }

    protected Result(bool isSuccess, Error? error) : this()
    {
        IsSuccess = isSuccess;
        Error = error;
    }
    
    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new (false, error);
    
    public static implicit operator Result(Error error) => Failure(error);
}