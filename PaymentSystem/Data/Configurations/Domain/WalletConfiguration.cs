using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");
        builder.HasKey(wallet => wallet.WalletId);
        builder.Property(wallet => wallet.WalletId).ValueGeneratedNever();

        builder.ComplexProperty(wallet => wallet.CurrentBalance, money =>
        {
            money.Property(m => m.Amount).HasColumnName("CurrentBalance").HasPrecision(Money.Precision, Money.Scale);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });

        // Two settlements crediting one seller at once conflict instead of one overwriting the other's credit.
        builder.Property<uint>("xmin").IsRowVersion();

        // One wallet per user per currency is a unique (UserId, Currency) index, but EF Core 10 cannot index a
        // complex property's column, so it is not declared here: InitialCreate creates it by hand. It also serves
        // lookups by UserId alone. Move it here once on EF Core 11.
    }

    public const string UserCurrencyIndex = "IX_Wallets_UserId_Currency";
}
