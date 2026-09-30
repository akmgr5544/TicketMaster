namespace PaymentProvider.Models;

public enum PaymentStatus
{
    AwaitingPaymentMethod,
    RequiresAction,
    Processing,
    Authorized,
    Succeeded,

    // The latest attempt failed. The customer can still retry with another payment method, so this
    // is not final for the order; only Canceled is.
    Failed,
    Canceled,
}
