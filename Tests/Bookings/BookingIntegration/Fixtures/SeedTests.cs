using Bookings.Application.Dtos;
using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Fixtures;

public sealed class SeedTests : IntegrationTest
{
    public SeedTests(BookingsFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Seeded_tickets_reach_the_database_with_real_keys()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");

        Assert.All(tickets, ticket => Assert.True(ticket.Id > 0));

        var stored = await ReadAsync(context =>
            context.Tickets.OrderBy(t => t.Id).ToArrayAsync());

        Assert.Equal(2, stored.Length);
        Assert.Equal(["A1", "A2"], stored.Select(t => t.Seat));
        Assert.All(stored, ticket => Assert.Equal(TicketStatus.None, ticket.Status));
    }

    [Fact]
    public async Task A_seeded_booking_leaves_its_tickets_booked()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.BookingAsync("user-1", tickets[0].Id);

        var stored = await ReadAsync(context =>
            context.Tickets.SingleAsync(t => t.Id == tickets[0].Id));

        Assert.Equal(TicketStatus.Booked, stored.Status);
    }

    [Fact]
    public async Task A_seeded_reservation_is_readable_under_its_namespaced_key()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

        var cache = Act.GetRequiredService<ICacheService>();
        var held = await cache.GetByKeysAsync<ReserveTicketDto>(
            [ReservationKeys.Reservation(tickets[0].Id)]);

        var reservation = Assert.Single(held);
        Assert.Equal("user-1", reservation.UserId);
        Assert.Equal("evt-1", reservation.EventId);
    }

    [Fact]
    public async Task Each_test_starts_from_an_empty_database()
    {
        // Depends on nothing this class seeded. If reset is broken, rows from the tests above survive
        // and this fails — which is the point.
        var count = await ReadAsync(context => context.Tickets.CountAsync());

        Assert.Equal(0, count);
    }
}
