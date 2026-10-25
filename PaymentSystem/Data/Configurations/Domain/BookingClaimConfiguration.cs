using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class BookingClaimConfiguration : IEntityTypeConfiguration<BookingClaim>
{
    public void Configure(EntityTypeBuilder<BookingClaim> builder)
    {
        builder.ToTable("BookingClaims");
        // The key is the whole point: two inserts for one booking serialise on it.
        builder.HasKey(claim => claim.BookingId);
        builder.Property(claim => claim.BookingId).ValueGeneratedNever();
        builder.Property(claim => claim.Status).HasConversion<string>().HasMaxLength(20);
    }
}
