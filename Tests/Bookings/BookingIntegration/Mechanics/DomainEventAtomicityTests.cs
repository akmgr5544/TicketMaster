using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Mechanics;

/// <summary>
/// The interceptor dispatches domain events after the write, so a handler's own save has to be
/// atomic with the write that triggered it. That holds only while the handler resolves the same
/// scoped <c>BookingDomainContext</c> the caller is saving through — a captive dependency on the
/// root container silently moves the handler onto its own connection, outside the transaction,
/// and the failure is invisible until something rolls back.
/// </summary>
public sealed class DomainEventAtomicityTests : IntegrationTest
{
    public DomainEventAtomicityTests(BookingsFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_handler_saves_through_the_requests_context()
    {
        var tickets = await Seed.TicketsAsync("evt-atomic-1", "A1");

        var context = Act.GetRequiredService<BookingDomainContext>();
        context.Bookings.Add(Booking.Create("user-1", BookingStatus.Booked, [tickets[0].Id]));
        await context.SaveChangesAsync();

        // The handler loaded and booked this ticket. If it used the caller's context, that context
        // is now tracking it. Zero means the handler ran somewhere else entirely.
        var trackedByCaller = context.ChangeTracker.Entries<Ticket>().Count();

        Assert.Equal(1, trackedByCaller);
    }

    [Fact]
    public async Task A_rolled_back_booking_leaves_its_seats_on_sale()
    {
        var tickets = await Seed.TicketsAsync("evt-atomic-2", "A1");
        var ticketId = tickets[0].Id;

        var context = Act.GetRequiredService<BookingDomainContext>();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Bookings.Add(Booking.Create("user-1", BookingStatus.Booked, [ticketId]));
            await context.SaveChangesAsync();

            // Exactly what TransactionBehavior does when a handler throws.
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await ReadAsync(c => c.Bookings.AsNoTracking().CountAsync()));

        var ticket = await ReadAsync(c =>
            c.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId));

        // Booked here means the seat is stranded: no booking refers to it and nothing will release it.
        Assert.Equal(TicketStatus.None, ticket.Status);
    }
}
