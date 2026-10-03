using Microsoft.EntityFrameworkCore;
using Bookings.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookings.Sql.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedOnAdd();
        builder.Property(b => b.UserId).IsRequired();
        builder.Property(b => b.CreatedAt).IsRequired();

        builder.OwnsMany(b => b.BookedTickets,
            bt =>
            {
                bt.ToTable("BookedTickets");
                bt.WithOwner().HasForeignKey("BookingId");
                bt.HasKey(b => b.Id);
                bt.Property(b => b.Id).ValueGeneratedOnAdd();
                bt.Property(b => b.RefundId);
            });

        builder.OwnsMany(b => b.Refunds,
            refund =>
            {
                refund.ToTable("BookingRefunds");
                refund.WithOwner().HasForeignKey("BookingId");
                refund.HasKey(r => r.Id);
                // Generated in the domain, so the id can go out in RefundRequested before the row is saved.
                refund.Property(r => r.Id).ValueGeneratedNever();
                refund.Property(r => r.Amount).HasPrecision(18, 2);
                refund.Property(r => r.Currency).HasMaxLength(3);
                refund.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
                refund.Property(r => r.CreatedAt).IsRequired();
            });
        builder.Navigation(b => b.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(b => b.BookingHistories,
            bh =>
            {
                bh.ToTable("BookingHistories");
                bh.WithOwner().HasForeignKey("BookingId");
                bh.HasKey(b => b.Id);
                bh.Property(b => b.Id).ValueGeneratedOnAdd();
            });
    }
}