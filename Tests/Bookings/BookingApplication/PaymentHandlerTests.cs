using Bookings.Application.DomainEventHandlers;
using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Enums;
using BookingApplication.Fakes;
using Bookings.Application.CommandHandlers.Bookings;
using Bookings.Application.Commands;

namespace BookingApplication;

/// <summary>
/// Settling a booking once the payment service says what happened. Two properties are asserted
/// throughout, both because payment results are delivered at least once and unordered: applying the
/// same outcome twice must land where applying it once did, and the outcome that lands first must be
/// the one that sticks.
/// </summary>
public class PaymentHandlerTests
{
    private const string EventId = "event-1";

    private static readonly DateTime StartDate = new(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc);

    private readonly FakeBookingRepository _bookings = new();
    private readonly FakeTicketsRepository _tickets = new();

    private static Ticket ABookedTicket(long id, string seat)
    {
        var ticket = new Ticket(seat, "venue-1", EventId, StartDate);
        ticket.Id = id;
        ticket.Book();
        return ticket;
    }

    /// <summary>
    /// Seeds a booking the way the booking flow leaves one: status Booked, its tickets booked, and its
    /// creation event already dispatched.
    /// </summary>
    private async Task<Booking> ABookingFor(params long[] ticketIds)
    {
        _tickets.Seed(ticketIds.Select(id => ABookedTicket(id, $"A{id}")).ToArray());

        var booking = Booking.Create("user-1", BookingStatus.Booked, ticketIds);
        booking.ClearDomainEvents();
        await _bookings.AddAsync(booking);
        return booking;
    }

    private Task Confirm(long bookingId) =>
        new ConfirmBookingCommandHandler(_bookings)
            .Handle(new ConfirmBookingPaymentCommand(bookingId), CancellationToken.None);

    private Task Release(long bookingId) =>
        new ReleaseUnpaidBookingCommandHandler(_bookings)
            .Handle(new ReleaseUnpaidBookingCommand(bookingId), CancellationToken.None);

    private Task ReleaseTickets(Booking booking) =>
        new BookingCancelledDomainEventHandler(_tickets)
            .Handle((BookingCancelledDomainEvent)booking.DomainEvents.Single(), CancellationToken.None);

    // --- Payment succeeded ---

    [Fact]
    public async Task Payment_marks_the_booking_paid_and_saves()
    {
        var booking = await ABookingFor(7);

        await Confirm(booking.Id);

        Assert.Equal(BookingStatus.Payed, booking.Status);
        Assert.Equal(1, _bookings.SaveCount);
    }

    /// <summary>
    /// The seats were taken when the booking was made, so paying changes nothing about them.
    /// </summary>
    [Fact]
    public async Task Payment_leaves_the_tickets_booked()
    {
        var booking = await ABookingFor(7);

        await Confirm(booking.Id);

        Assert.All(_tickets.Tickets, ticket => Assert.Equal(TicketStatus.Booked, ticket.Status));
    }

    // --- Payment failed ---

    [Fact]
    public async Task A_failed_payment_cancels_the_booking()
    {
        var booking = await ABookingFor(7, 9);

        await Release(booking.Id);

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal(1, _bookings.SaveCount);
    }

    /// <summary>
    /// The point of the whole path: seats nobody paid for go back on sale.
    /// </summary>
    [Fact]
    public async Task A_failed_payment_puts_the_seats_back_on_sale()
    {
        var booking = await ABookingFor(7, 9);

        await Release(booking.Id);
        await ReleaseTickets(booking);

        Assert.All(_tickets.Tickets, ticket => Assert.Equal(TicketStatus.None, ticket.Status));
        Assert.Equal(1, _tickets.SaveCount);
    }

    /// <summary>
    /// A seat cancelled because the event was called off stays cancelled — it must not be resold just
    /// because the payment for it also failed.
    /// </summary>
    [Fact]
    public async Task A_failed_payment_does_not_revive_a_ticket_the_event_cancelled()
    {
        var booking = await ABookingFor(7);
        _tickets.Tickets.Single().Cancel(eventVersion: 2);

        await Release(booking.Id);
        await ReleaseTickets(booking);

        Assert.Equal(TicketStatus.Cancelled, _tickets.Tickets.Single().Status);
    }

    // --- Redelivery and racing outcomes ---

    [Fact]
    public async Task The_same_payment_arriving_twice_settles_the_booking_once()
    {
        var booking = await ABookingFor(7);

        await Confirm(booking.Id);
        await Confirm(booking.Id);

        Assert.Equal(BookingStatus.Payed, booking.Status);
        Assert.Equal(2, booking.BookingHistories.Count);
    }

    [Fact]
    public async Task The_same_failure_arriving_twice_releases_the_seats_once()
    {
        var booking = await ABookingFor(7);

        await Release(booking.Id);
        await Release(booking.Id);

        Assert.Single(booking.DomainEvents);
    }

    [Fact]
    public async Task A_failure_arriving_after_payment_leaves_the_booking_paid()
    {
        var booking = await ABookingFor(7);
        await Confirm(booking.Id);

        await Assert.ThrowsAsync<BookingsDomainException>(() => Release(booking.Id));
        Assert.Equal(BookingStatus.Payed, booking.Status);
    }

    [Fact]
    public async Task A_payment_arriving_after_a_failure_leaves_the_booking_cancelled()
    {
        var booking = await ABookingFor(7);
        await Release(booking.Id);

        await Assert.ThrowsAsync<BookingsDomainException>(() => Confirm(booking.Id));
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    // --- Missing booking ---

    [Fact]
    public async Task Refuses_to_confirm_a_booking_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Confirm(404));
    }

    [Fact]
    public async Task Refuses_to_release_a_booking_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Release(404));
    }
}
