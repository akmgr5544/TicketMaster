using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class SettlementDiscrepancyConfiguration : IEntityTypeConfiguration<SettlementDiscrepancy>
{
    public void Configure(EntityTypeBuilder<SettlementDiscrepancy> builder)
    {
        builder.ToTable("SettlementDiscrepancies");
        builder.HasKey(discrepancy => discrepancy.SettlementDiscrepancyId);
        builder.Property(discrepancy => discrepancy.SettlementDiscrepancyId).ValueGeneratedNever();

        builder.Property(discrepancy => discrepancy.Provider).HasMaxLength(20);
        builder.Property(discrepancy => discrepancy.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(discrepancy => discrepancy.Reference).HasMaxLength(255);
        builder.Property(discrepancy => discrepancy.ExpectedAmount).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(discrepancy => discrepancy.ExpectedCurrency).HasMaxLength(3);
        builder.Property(discrepancy => discrepancy.ActualAmount).HasPrecision(Money.Precision, Money.Scale);
        builder.Property(discrepancy => discrepancy.ActualCurrency).HasMaxLength(3);
        builder.Property(discrepancy => discrepancy.Detail).HasMaxLength(500);

        builder.HasIndex(discrepancy => new { discrepancy.Provider, discrepancy.Kind, discrepancy.Reference }).IsUnique();
    }
}
