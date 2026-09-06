using Bookings.Application.Dtos;
using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Fixtures;


public sealed class Seed
{
    private readonly IServiceProvider _root;

    public Seed(IServiceProvider root)
    {
        _root = root;
    }

    /// <summary>Inside the sale window, and Utc because Npgsql rejects any other Kind.</summary>
    public static DateTime Soon => DateTime.UtcNow.AddDays(7);

    /// <summary>Outside Ticket.SaleGracePeriod, so the seat is no longer sellable.</summary>
    public static DateTime LongPast => DateTime.UtcNow.AddHours(-6);

    public Task<Ticket[]> TicketsAsync(string eventId, params string[] seats) =>
        TicketsAsync(eventId, Soon, eventVersion: 0, seats);

    public async Task<Ticket[]> TicketsAsync(string eventId,
        DateTime eventDate,
        long eventVersion,
        params string[] seats)
    {
        await using var scope = _root.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        var tickets = seats
            .Select(seat => new Ticket(seat, $"venue-for-{eventId}", eventId, eventDate, eventVersion))
            .ToArray();

        context.Tickets.AddRange(tickets);
        await context.SaveChangesAsync();

        return tickets;
    }

    /// <summary>Tickets that exist but are cancelled — what reconciliation treats as uncovered.</summary>
    public async Task<Ticket[]> CancelledTicketsAsync(string eventId, params string[] seats)
    {
        await using var scope = _root.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        var tickets = seats
            .Select(seat =>
            {
                var ticket = new Ticket(seat, $"venue-for-{eventId}", eventId, Soon);
                ticket.Cancel(eventVersion: 1);
                return ticket;
            })
            .ToArray();

        context.Tickets.AddRange(tickets);
        await context.SaveChangesAsync();

        return tickets;
    }

    public async Task<Booking> BookingAsync(string userId, params long[] ticketIds)
    {
        await using var scope = _root.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        var booking = Booking.Create(userId, BookingStatus.Booked, ticketIds);

        // Create raises BookingCreatedDomainEvent, whose handler books the tickets. Seeding through
        // the context means the interceptor dispatches it, so the seeded state is coherent. That
        // dispatch happens in this method's own scope, though, not the test's Act scope - a test that
        // seeds a booking and then asserts a publish count via BookingCreatedPublishCounter resolved
        // from Act would silently observe 0.
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        return booking;
    }

    public async Task ReservationAsync(string userId, string eventId, params long[] ticketIds)
    {
        await using var scope = _root.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        var entries = ticketIds
            .Select(id => new KeyValuePair<string, ReserveTicketDto>(
                ReservationKeys.Reservation(id),
                new ReserveTicketDto(id, eventId, userId)))
            .ToArray();

        await cache.SetToCacheAsync(entries, TimeSpan.FromMinutes(5));
    }
}
