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

        builder.OwnsMany(b => b.BookedTickets,
            bt =>
            {
                bt.ToTable("BookedTickets");
                bt.WithOwner().HasForeignKey("BookingId");
                bt.HasKey(b => b.Id);
                bt.Property(b => b.Id).ValueGeneratedOnAdd();
            });

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