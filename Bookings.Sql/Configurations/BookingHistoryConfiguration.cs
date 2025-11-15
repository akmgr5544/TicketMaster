using Bookings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookings.Sql.Configurations;

public class BookingHistoryConfiguration : IEntityTypeConfiguration<BookingHistory>
{
    public void Configure(EntityTypeBuilder<BookingHistory> builder)
    {
        throw new NotImplementedException();
    }
}