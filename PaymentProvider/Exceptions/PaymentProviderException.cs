using PaymentProvider.Models;

namespace PaymentProvider.Exceptions;

public sealed class PaymentProviderException(
    PaymentProviderKind provider,
    PaymentProviderErrorKind kind,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public PaymentProviderKind Provider { get; } = provider;

    public PaymentProviderErrorKind Kind { get; } = kind;

    public bool IsRetryable => Kind == PaymentProviderErrorKind.Transient;
}
