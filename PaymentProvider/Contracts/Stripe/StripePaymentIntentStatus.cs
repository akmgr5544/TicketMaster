namespace PaymentProvider.Contracts.Stripe;

internal enum StripePaymentIntentStatus
{
    RequiresPaymentMethod,
    RequiresConfirmation,
    RequiresAction,
    Processing,
    RequiresCapture,
    Succeeded,
    Canceled,
    Unknown,
}
