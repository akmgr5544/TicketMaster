using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("PaymentMethods");
        builder.HasKey(method => method.Id);
        builder.Property(method => method.Id).ValueGeneratedNever();

        builder.Property(method => method.UserId).HasMaxLength(64).IsRequired();
        builder.Property(method => method.PspName).HasMaxLength(50).IsRequired();
        builder.Property(method => method.PspToken).HasMaxLength(255).IsRequired();

        builder.HasIndex(method => method.UserId, "IX_PaymentMethods_UserId");

        // One default per user, held by the database: two requests changing the default at once cannot both win.
        builder.HasIndex(method => method.UserId, DefaultPerUserIndex)
            .IsUnique()
            .HasFilter("\"IsDefault\"");
    }

    public const string DefaultPerUserIndex = "IX_PaymentMethods_UserId_Default";
}
