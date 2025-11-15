using Microsoft.EntityFrameworkCore;
using Bookings.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookings.Sql.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        throw new NotImplementedException();
    }
}