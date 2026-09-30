using PaymentSystem.Domain.Exceptions;

namespace PaymentSystem.Domain.Shared;

internal static class CurrencyCode
{
    public static void EnsureValid(string currency)
    {
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
            throw new PaymentDomainException("Currency must be an upper-case ISO 4217 code.");
    }
}
