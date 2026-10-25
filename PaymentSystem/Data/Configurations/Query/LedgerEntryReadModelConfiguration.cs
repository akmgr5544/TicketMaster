using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Query;

namespace PaymentSystem.Data.Configurations.Query;

internal sealed class LedgerEntryReadModelConfiguration : IEntityTypeConfiguration<LedgerEntryReadModel>
{
    public void Configure(EntityTypeBuilder<LedgerEntryReadModel> builder)
    {
        builder.ToTable("LedgerEntries", table => table.ExcludeFromMigrations());
        builder.HasKey(entry => entry.EntryId);
    }
}
