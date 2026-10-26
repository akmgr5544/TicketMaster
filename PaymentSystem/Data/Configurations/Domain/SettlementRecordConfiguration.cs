using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class SettlementRecordConfiguration : IEntityTypeConfiguration<SettlementRecord>
{
    public void Configure(EntityTypeBuilder<SettlementRecord> builder)
    {
        builder.ToTable("SettlementRecords");
        builder.HasKey(record => record.SettlementRecordId);
        builder.Property(record => record.SettlementRecordId).ValueGeneratedNever();

        builder.Property(record => record.Provider).HasMaxLength(20);
        builder.Property(record => record.ProviderReference).HasMaxLength(255);
        builder.Property(record => record.Kind).HasConversion<string>().HasMaxLength(20);
        builder.ComplexProperty(record => record.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(Money.Precision, Money.Scale);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });

        builder.HasIndex(record => new { record.Provider, record.ProviderReference, record.Kind }).IsUnique();
    }
}
