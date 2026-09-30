using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");
        builder.HasKey(wallet => wallet.WalletId);
        builder.Property(wallet => wallet.WalletId).ValueGeneratedNever();
        builder.Ignore(wallet => wallet.DomainEvents);

        builder.HasIndex(wallet => new { wallet.OwnerId, wallet.Currency }).IsUnique();
        builder.Property(wallet => wallet.OwnerId).IsRequired();
        builder.Property(wallet => wallet.Balance).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale).IsRequired();
        builder.Property(wallet => wallet.Currency).HasMaxLength(3).IsRequired();
        builder.Property(wallet => wallet.CreatedAt).IsRequired();
        builder.Property(wallet => wallet.UpdatedAt).IsRequired();

        // Two orders for the same seller settling at once each read the balance and add to it; without a
        // concurrency token the later save silently overwrites the earlier credit. Mapped onto Postgres'
        // xmin system column, so there is no column to maintain and nothing in the domain.
        builder.Property<uint>("Version").IsRowVersion();
    }
}
