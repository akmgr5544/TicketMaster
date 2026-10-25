using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Query;

namespace PaymentSystem.Data.Configurations.Query;

internal sealed class WalletReadModelConfiguration : IEntityTypeConfiguration<WalletReadModel>
{
    public void Configure(EntityTypeBuilder<WalletReadModel> builder)
    {
        builder.ToTable("Wallets", table => table.ExcludeFromMigrations());
        builder.HasKey(wallet => wallet.WalletId);
    }
}
