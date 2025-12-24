using Bookings.Domain.Entities;
using Bookings.Sql.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Sql;

public class BookingDomainContext : DbContext
{
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<Ticket> Tickets { get; set; }

    public BookingDomainContext(DbContextOptions options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new BookingConfiguration());
        modelBuilder.ApplyConfiguration(new TicketConfiguration());
    }
}