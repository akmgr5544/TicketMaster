using Bookings.Application.DomainEventHandlers;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Sql;
using Bookings.Sql.Interceptors;
using Bookings.Sql.Repositories;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BookingIntegration;

/// <summary>
/// The real dispatch chain against a real relational provider: save a booking, let the interceptor
/// publish its creation event, and let the handler for that event load the tickets, book them, and
/// save again — a save that begins while the first one's completion callback is still on the stack.
/// <para>
/// Everything here is the production wiring apart from the provider being SQLite. Fakes cannot answer
/// the two questions that matter: whether EF tolerates that nested save at all, and whether clearing
/// domain events before publishing is what stops the second save re-publishing them forever.
/// </para>
/// </summary>
public sealed class DomainEventDispatchTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private BookingDomainContext _context = null!;
    private RecordingPublisher _publisher = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _publisher = new RecordingPublisher();

        var options = new DbContextOptionsBuilder<BookingDomainContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new DomainEventPublisherInterceptor(_publisher))
            .Options;

        _context = new BookingDomainContext(options);
        await _context.Database.EnsureCreatedAsync();

        _publisher.Handle = notification => new BookingCreatedDomainEventHandler(
                new TicketsRepository(_context))
            .Handle((Bookings.Domain.DomainEvents.BookingCreatedDomainEvent)notification,
                CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Ticket[]> SeedTicketsAsync(params string[] seats)
    {
        var tickets = seats
            .Select(seat => new Ticket(seat, "venue-1", "event-1",
                new DateTime(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc)))
            .ToArray();

        _context.Tickets.AddRange(tickets);
        await _context.SaveChangesAsync();
        return tickets;
    }

    /// <summary>
    /// The original bug: the status was set on an entity nobody saved, so it never reached the
    /// database. Asserted by reading the rows back through a fresh change tracker.
    /// </summary>
    [Fact]
    public async Task Booking_marks_its_tickets_booked_in_the_database()
    {
        var tickets = await SeedTicketsAsync("A1", "A2");
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());

        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var stored = await _context.Tickets.AsNoTracking().ToArrayAsync();
        Assert.All(stored, ticket => Assert.Equal(TicketStatus.Booked, ticket.Status));
    }

    /// <summary>
    /// The handler's own save re-enters the interceptor. Without clearing the events first, the same
    /// creation event would be published again from the still-tracked booking, and the handler would
    /// run again with it — recursion, not a duplicate delivery.
    /// </summary>
    [Fact]
    public async Task Publishes_the_creation_event_exactly_once()
    {
        var tickets = await SeedTicketsAsync("A1");
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());

        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();

        Assert.Equal(1, _publisher.PublishCount);
        Assert.Empty(booking.DomainEvents);
    }

    /// <summary>
    /// A second save of an aggregate whose events have already been dispatched must publish nothing.
    /// </summary>
    [Fact]
    public async Task Saving_again_republishes_nothing()
    {
        var tickets = await SeedTicketsAsync("A1");
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());

        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();
        await _context.SaveChangesAsync();

        Assert.Equal(1, _publisher.PublishCount);
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public int PublishCount { get; private set; }

        public Func<INotification, Task> Handle { get; set; } = _ => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Publish((INotification)notification, cancellationToken);

        public Task Publish<TNotification>(TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            PublishCount++;
            return Handle(notification);
        }
    }
}
