namespace PaymentProvider.Exceptions;

public enum PaymentProviderErrorKind
{
    InvalidRequest,
    Transient,
    Configuration,
    InvalidSignature,
    Unknown,
}
