using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Mechanics;

/// <summary>
/// <c>MakeBookingCommand</c> returns the new booking's id so the endpoint can answer 201, and
/// <c>Booking.Id</c> is init-only. That combination only works if the provider writes the generated
/// key back onto the tracked instance through the compiler-generated backing field — which is worth
/// asserting rather than assuming, because the endpoint's response depends on it.
/// </summary>
public sealed class BookingKeyTests : IntegrationTest
{
    public BookingKeyTests(BookingsFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task A_saved_booking_knows_the_key_the_database_gave_it()
    {
        var tickets = await Seed.TicketsAsync("event-1", "A1");
        var context = Act.GetRequiredService<BookingDomainContext>();
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());
        Assert.Equal(0, booking.Id);

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        Assert.NotEqual(0, booking.Id);
    }

    /// <summary>
    /// And the key really is the row's, not just a counter in memory.
    /// </summary>
    [Fact]
    public async Task The_key_it_reports_is_the_one_it_can_be_read_back_by()
    {
        var tickets = await Seed.TicketsAsync("event-1", "A1");
        var context = Act.GetRequiredService<BookingDomainContext>();
        var booking = Booking.Create("user-1", BookingStatus.Booked, tickets.Select(t => t.Id).ToArray());
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        var stored = await ReadAsync(db => db.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == booking.Id));

        Assert.NotNull(stored);
        Assert.Equal("user-1", stored.UserId);
    }
}
