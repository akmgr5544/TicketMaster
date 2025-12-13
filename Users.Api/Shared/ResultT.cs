namespace Users.Api.Shared;

public record Result<T> : Result
{
    public T? Value { get; set; }

    private Result(T value) : base(true, null)
    {
        Value = value;
    }

    private Result(Error error) : base(false, error)
    {
    }

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);
}