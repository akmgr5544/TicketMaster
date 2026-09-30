namespace PaymentSystem.Shared.Results;

public sealed record Result<T> : Result
{
    public T? Value { get; }

    private Result(T value) : base(true, null) => Value = value;

    private Result(Error error) : base(false, error)
    {
    }

    public static Result<T> Success(T value) => new(value);
    public new static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new(error);
}
