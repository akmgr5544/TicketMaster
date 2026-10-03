using Bookings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookings.Sql.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.ComplexProperty(p => p.Pricing, pricing =>
        {
            pricing.Property(p => p.Price).HasColumnName("Price").HasPrecision(18, 2);
            pricing.Property(p => p.Currency).HasColumnName("Currency").HasMaxLength(3);
            pricing.Property(p => p.SellerId).HasColumnName("SellerId");
        });
    }
}