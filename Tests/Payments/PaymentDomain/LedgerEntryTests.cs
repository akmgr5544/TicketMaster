using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentDomain;

public sealed class LedgerEntryTests
{
    [Fact]
    public void An_entry_carries_what_it_records()
    {
        var reference = Guid.NewGuid();
        var amount = Money.Create(12.30m, "USD");

        var entry = LedgerEntry.Create(reference, RefType.Payment, "seller-1", EntryType.Credit, amount);

        Assert.Equal(reference, entry.TransactionRefId);
        Assert.Equal(RefType.Payment, entry.RefType);
        Assert.Equal("seller-1", entry.AccountId);
        Assert.Equal(EntryType.Credit, entry.EntryType);
        Assert.Equal(amount, entry.Amount);
    }

    [Fact]
    public void An_entry_for_nothing_is_refused() =>
        Assert.Throws<PaymentDomainException>(() =>
            LedgerEntry.Create(Guid.NewGuid(), RefType.Refund, "buyer-1", EntryType.Debit, Money.Zero("USD")));
}
