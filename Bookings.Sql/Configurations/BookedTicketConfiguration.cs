using Bookings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookings.Sql.Configurations;

public class BookedTicketConfiguration : IEntityTypeConfiguration<BookedTicket>
{
    public void Configure(EntityTypeBuilder<BookedTicket> builder)
    {
        throw new NotImplementedException();
    }
}