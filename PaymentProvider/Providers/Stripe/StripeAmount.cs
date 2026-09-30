using System.Collections.Frozen;
using System.Globalization;
using PaymentProvider.Models;

namespace PaymentProvider.Providers.Stripe;

// Stripe takes integer minor units, and the exponent differs by currency: a fixed ×100 would charge
// 100× too much in JPY. Lists from docs.stripe.com/currencies.
internal static class StripeAmount
{
    // The widest amount Stripe documents (12 digits, for cards); anything larger is refused here
    // rather than overflowing the conversion.
    private const decimal MaxMinorUnits = 999_999_999_999m;

    private static readonly FrozenSet<string> ZeroDecimal = new[]
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Sent as two-decimal amounts, but Stripe accepts only whole units (5 ISK is 500).
    private static readonly FrozenSet<string> WholeUnitsInTwoDecimals = new[] { "ISK", "UGX" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // The last minor-unit digit must be 0, so these are effectively charged to two decimals.
    private static readonly FrozenSet<string> ThreeDecimal = new[] { "BHD", "JOD", "KWD", "OMR", "TND" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static long ToMinorUnits(decimal amount, string currency)
    {
        RequestGuard.Amount(PaymentProviderKind.Stripe, amount);

        if (WholeUnitsInTwoDecimals.Contains(currency) && decimal.Truncate(amount) != amount)
        {
            throw Invalid($"{currency} amounts must be whole units, was {Format(amount)}.");
        }

        var factor = Factor(currency);
        if (amount > MaxMinorUnits / factor)
        {
            throw Invalid($"{Format(amount)} {currency} is more than Stripe can charge.");
        }

        var minorUnits = amount * factor;
        if (decimal.Truncate(minorUnits) != minorUnits)
        {
            throw Invalid($"{Format(amount)} has more decimal places than {currency} allows.");
        }

        if (ThreeDecimal.Contains(currency) && minorUnits % 10 != 0)
        {
            throw Invalid($"{currency} amounts must be rounded to two decimal places, was {Format(amount)}.");
        }

        return (long)minorUnits;
    }

    private static decimal Factor(string currency)
    {
        if (ZeroDecimal.Contains(currency))
        {
            return 1m;
        }

        return ThreeDecimal.Contains(currency) ? 1000m : 100m;
    }

    private static string Format(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);

    private static Exception Invalid(string message) => RequestGuard.Invalid(PaymentProviderKind.Stripe, message);
}
