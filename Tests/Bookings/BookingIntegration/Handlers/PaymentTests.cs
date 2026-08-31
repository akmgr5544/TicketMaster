using Bookings.Application.Commands;
using Bookings.Application.Commands.Payments;
using Bookings.Application.Exceptions;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using BookingIntegration.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BookingIntegration.Handlers;

/// <summary>
/// Settling a booking once the payment service says what happened. Two properties are asserted
/// throughout, both because payment results are delivered at least once and unordered: applying the
/// same outcome twice must land where applying it once did, and the outcome that lands first must be
/// the one that sticks.
/// <para>
/// <c>ConfirmBookingCommand</c> and <c>ReleaseUnpaidBookingCommand</c> are both
/// <see cref="Bookings.Domain.Abstractions.ITransactionalRequest"/>, so the release path below runs the
/// real domain-event dispatch — <c>Booking.Cancel()</c> raises <c>BookingCancelledDomainEvent</c>,
/// whose handler releases the tickets — inside the caller's own transaction.
/// </para>
/// </summary>
public sealed class PaymentTests : IntegrationTest
{
    public PaymentTests(BookingsFixture fixture) : base(fixture)
    {
    }

    // --- Payment succeeded ---

    [Fact]
    public async Task Payment_marks_the_booking_paid_and_saves()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync("user-1", tickets[0].Id);

        await Sender.Send(new ConfirmBookingCommand(booking.Id));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking.Id));

        Assert.Equal(BookingStatus.Payed, stored.Status);
    }

    /// <summary>
    /// The seats were taken when the booking was made, so paying changes nothing about them.
    /// </summary>
    [Fact]
    public async Task Payment_leaves_the_tickets_booked()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();
        var booking = await Seed.BookingAsync("user-1", ids);

        await Sender.Send(new ConfirmBookingCommand(booking.Id));

        var stored = await ReadAsync(context =>
            context.Tickets.Where(t => ids.Contains(t.Id)).ToArrayAsync());

        Assert.All(stored, ticket => Assert.Equal(TicketStatus.Booked, ticket.Status));
    }

    // --- Payment failed ---

    [Fact]
    public async Task A_failed_payment_cancels_the_booking()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();
        var booking = await Seed.BookingAsync("user-1", ids);

        await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking.Id));

        Assert.Equal(BookingStatus.Cancelled, stored.Status);
    }

    /// <summary>
    /// The point of the whole path: seats nobody paid for go back on sale. Goes through the real
    /// domain-event dispatch — <c>Booking.Cancel()</c> raises the event that releases the tickets, and
    /// that chain has to run on the caller's own context inside the caller's own transaction.
    /// </summary>
    [Fact]
    public async Task A_failed_payment_puts_the_seats_back_on_sale()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();
        var booking = await Seed.BookingAsync("user-1", ids);

        await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));

        var stored = await ReadAsync(context =>
            context.Tickets.Where(t => ids.Contains(t.Id)).ToArrayAsync());

        Assert.All(stored, ticket => Assert.Equal(TicketStatus.None, ticket.Status));
    }

    /// <summary>
    /// A seat cancelled because the event was called off stays cancelled — it must not be resold just
    /// because the payment for it also failed. Cancelled through <c>CancelEventTicketsCommand</c>
    /// rather than by mutating the entity directly, so this proves the real interaction between the two
    /// commands rather than an assumption about <c>Ticket.Release()</c>'s guard.
    /// <para>
    /// The two tickets belong to different events so that cancelling one event's tickets leaves the
    /// other untouched — the sibling is what proves the release path still puts an ordinary seat back
    /// on sale even while the cancelled one is left alone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_failed_payment_does_not_revive_a_ticket_the_event_cancelled()
    {
        var cancelledEvent = await Seed.TicketsAsync("evt-1", "A1");
        var otherEvent = await Seed.TicketsAsync("evt-2", "B1");
        var booking = await Seed.BookingAsync("user-1", cancelledEvent[0].Id, otherEvent[0].Id);

        await Sender.Send(new CancelEventTicketsCommand("evt-1", Version: 1));
        await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));

        var stored = await ReadAsync(context => context.Tickets
            .Where(t => t.Id == cancelledEvent[0].Id || t.Id == otherEvent[0].Id)
            .ToDictionaryAsync(t => t.Id));

        Assert.Equal(TicketStatus.Cancelled, stored[cancelledEvent[0].Id].Status);
        Assert.Equal(TicketStatus.None, stored[otherEvent[0].Id].Status);
    }

    // --- Redelivery and racing outcomes ---

    [Fact]
    public async Task The_same_payment_arriving_twice_settles_the_booking_once()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync("user-1", tickets[0].Id);

        await Sender.Send(new ConfirmBookingCommand(booking.Id));
        await Sender.Send(new ConfirmBookingCommand(booking.Id));

        var stored = await ReadAsync(context => context.Bookings
            .Include(b => b.BookingHistories)
            .SingleAsync(b => b.Id == booking.Id));

        Assert.Equal(BookingStatus.Payed, stored.Status);
        // Created + Payed. A third row means MarkPaid() appended twice despite the early-return guard.
        Assert.Equal(2, stored.BookingHistories.Count);
    }

    [Fact]
    public async Task The_same_failure_arriving_twice_releases_the_seats_once()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync("user-1", tickets[0].Id);

        await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));
        await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));

        var stored = await ReadAsync(context => context.Bookings
            .Include(b => b.BookingHistories)
            .SingleAsync(b => b.Id == booking.Id));

        Assert.Equal(BookingStatus.Cancelled, stored.Status);
        // Created + Cancelled. A third row means Cancel() raised its event twice, which would release a
        // seat somebody else may have taken by then.
        Assert.Equal(2, stored.BookingHistories.Count);
    }

    [Fact]
    public async Task A_failure_arriving_after_payment_leaves_the_booking_paid()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync("user-1", tickets[0].Id);
        await Sender.Send(new ConfirmBookingCommand(booking.Id));

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id)));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking.Id));

        Assert.Equal(BookingStatus.Payed, stored.Status);
    }

    [Fact]
    public async Task A_payment_arriving_after_a_failure_leaves_the_booking_cancelled()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var booking = await Seed.BookingAsync("user-1", tickets[0].Id);
        await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new ConfirmBookingCommand(booking.Id)));

        var stored = await ReadAsync(context => context.Bookings.SingleAsync(b => b.Id == booking.Id));

        Assert.Equal(BookingStatus.Cancelled, stored.Status);
    }

    // --- Missing booking ---

    [Fact]
    public async Task Refuses_to_confirm_a_booking_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new ConfirmBookingCommand(long.MaxValue)));
    }

    [Fact]
    public async Task Refuses_to_release_a_booking_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new ReleaseUnpaidBookingCommand(long.MaxValue)));
    }
}
