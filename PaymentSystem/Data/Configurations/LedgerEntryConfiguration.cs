using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations;

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries");
        // Entries are append-only and never looked up individually, so the key lives only in the database.
        builder.Property<Guid>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        // One debit and one credit per order: a redelivered settlement that tries to record the pair again
        // fails here instead of doubling the ledger.
        builder.HasIndex(entry => new { entry.PaymentOrderId, entry.Type }).IsUnique();
        builder.HasOne<PaymentOrder>()
            .WithMany()
            .HasForeignKey(entry => entry.PaymentOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(entry => entry.AccountId).IsRequired();
        builder.Property(entry => entry.Type).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(entry => entry.Amount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale).IsRequired();
        builder.Property(entry => entry.Currency).HasMaxLength(3).IsRequired();
        builder.Property(entry => entry.CreatedAt).IsRequired();
    }
}
