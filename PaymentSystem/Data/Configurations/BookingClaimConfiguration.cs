using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;

namespace PaymentSystem.Data.Configurations;

internal sealed class BookingClaimConfiguration : IEntityTypeConfiguration<BookingClaim>
{
    public void Configure(EntityTypeBuilder<BookingClaim> builder)
    {
        builder.ToTable("BookingClaims");
        builder.HasKey(claim => claim.BookingId);
        builder.Property(claim => claim.BookingId).ValueGeneratedNever();
        builder.Property(claim => claim.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(claim => claim.CreatedAt).IsRequired();
    }
}
