using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentProvider.Providers;

// Checks every adapter runs before a request leaves the process, so a bad input is an InvalidRequest
// the caller can act on rather than a provider round trip, a charge, or a NullReferenceException.
internal static class RequestGuard
{
    public static void PaymentOrderId(PaymentProviderKind provider, Guid paymentOrderId)
    {
        // The order id keys idempotency and is what a lost payment is found by; every empty-id order
        // would collide on it.
        if (paymentOrderId == Guid.Empty)
        {
            throw Invalid(provider, "PaymentOrderId must not be empty.");
        }
    }

    public static string Currency(PaymentProviderKind provider, string? currency)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetter))
        {
            throw Invalid(provider, $"'{currency}' is not an ISO 4217 currency code.");
        }

        return currency.ToUpperInvariant();
    }

    public static void Amount(PaymentProviderKind provider, decimal amount)
    {
        if (amount <= 0)
        {
            throw Invalid(provider, $"Amount must be positive, was {amount}.");
        }
    }

    public static void PaymentMethod(PaymentProviderKind provider, string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            throw Invalid(provider, "A payment method token is required.");
        }
    }

    public static PaymentProviderException Invalid(PaymentProviderKind provider, string message) =>
        new(provider, PaymentProviderErrorKind.InvalidRequest, message);
}
