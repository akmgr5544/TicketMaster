using PaymentSystem.Domain.Exceptions;

namespace PaymentSystem.Domain.Shared;

// The column shape every amount is stored in. The domain refuses what the column cannot hold exactly,
// because Postgres would otherwise round it on save — the PSP would charge one amount and the ledger
// record another.
internal static class MoneyAmount
{
    public const int Precision = 18;
    public const int Scale = 2;
    public const decimal MaxStorable = 9999999999999999.99m;

    public static void EnsurePositiveAndStorable(decimal amount, string what)
    {
        if (amount <= 0)
            throw new PaymentDomainException($"{what} must be positive.");
        // Compared by value, not by decimal.Scale: 10.100m carries scale 3 yet is exactly 10.10.
        if (decimal.Round(amount, Scale) != amount)
            throw new PaymentDomainException($"{what} cannot have more than {Scale} decimal places.");
        if (amount > MaxStorable)
            throw new PaymentDomainException($"{what} exceeds the largest storable amount.");
    }
}
