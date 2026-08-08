using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using Bookings.Sql.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Sql;

internal class BookingDomainContext : DbContext
{
    public DbSet<Booking> Bookings { get; set; }
    public DbSet<Ticket> Tickets { get; set; }
    public DbSet<BookedTicket> BookedTickets { get; set; }

    public BookingDomainContext(DbContextOptions<BookingDomainContext> options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity.DomainEvents plus its _domainEvents backing field looks like a collection
        // navigation to EF's conventions, which then tries to map DomainEvent as an entity type.
        // Domain events are dispatched in-process and never persisted.
        modelBuilder.Ignore<DomainEvent>();

        modelBuilder.ApplyConfiguration(new BookingConfiguration());
        modelBuilder.ApplyConfiguration(new TicketConfiguration());
    }
}