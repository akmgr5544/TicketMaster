using PaymentProvider.Exceptions;

namespace PaymentSystem.Shared.Results;

public sealed record Error(string Code, ErrorType Type, string Message)
{
    public static Error NotFound(string code, string message) => new(code, ErrorType.NotFound, message);
    public static Error BadRequest(string code, string message) => new(code, ErrorType.BadRequest, message);
    public static Error Conflict(string code, string message) => new(code, ErrorType.Conflict, message);

    // Null for a failure the caller cannot act on: a misconfigured or misbehaving provider is this service's
    // fault, and surfaces as a 500 rather than as a client error.
    public static Error? FromProvider(PaymentProviderException exception) => exception.Kind switch
    {
        PaymentProviderErrorKind.Transient => Conflict("psp_unavailable", "The payment provider is unavailable. Try again."),
        PaymentProviderErrorKind.InvalidRequest => BadRequest("psp_rejected_request", "The payment provider rejected the request."),
        _ => null
    };
}
