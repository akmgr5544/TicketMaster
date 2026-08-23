using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Sql;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Bookings.Application.Commands.Bookings;

namespace BookingIntegration;

/// <summary>
/// <c>MakeBookingCommand</c> returns the new booking's id so the endpoint can answer 201, and
/// <c>Booking.Id</c> is init-only. That combination only works if the provider writes the generated
/// key back onto the tracked instance through the compiler-generated backing field — which is worth
/// asserting rather than assuming, because the endpoint's response depends on it.
/// </summary>
public sealed class BookingKeyTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private BookingDomainContext _context = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _context = new BookingDomainContext(new DbContextOptionsBuilder<BookingDomainContext>()
            .UseSqlite(_connection)
            .Options);

        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_saved_booking_knows_the_key_the_database_gave_it()
    {
        var booking = Booking.Create("user-1", BookingStatus.Booked, [7L]);
        Assert.Equal(0, booking.Id);

        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();

        Assert.NotEqual(0, booking.Id);
    }

    /// <summary>
    /// And the key really is the row's, not just a counter in memory.
    /// </summary>
    [Fact]
    public async Task The_key_it_reports_is_the_one_it_can_be_read_back_by()
    {
        var booking = Booking.Create("user-1", BookingStatus.Booked, [7L]);
        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var stored = await _context.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == booking.Id);

        Assert.NotNull(stored);
        Assert.Equal("user-1", stored.UserId);
    }
}
