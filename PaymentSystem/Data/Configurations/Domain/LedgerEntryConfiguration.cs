using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries");
        builder.HasKey(entry => entry.EntryId);
        builder.Property(entry => entry.EntryId).ValueGeneratedOnAdd();

        builder.ComplexProperty(entry => entry.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(Money.Precision, Money.Scale);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });

        builder.Property(entry => entry.RefType).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.EntryType).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.AccountId).HasMaxLength(64).IsRequired();

        builder.HasIndex(entry => entry.TransactionRefId);
    }
}
