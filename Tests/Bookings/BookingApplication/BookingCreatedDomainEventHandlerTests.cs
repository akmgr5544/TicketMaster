using Bookings.Application.DomainEventHandlers;
using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using BookingApplication.Fakes;

namespace BookingApplication;

/// <summary>
/// This handler is what actually turns a ticket into a booked one. It previously set the status on
/// <c>Ticket</c> instances handed to it inside the event and saved nothing, so the change lived in the
/// change tracker until it was discarded — tickets never became <c>Booked</c>. Hence the deliberate
/// assertion on the save: booking the ticket in memory is only half the job.
/// </summary>
public class BookingCreatedDomainEventHandlerTests
{
    private const string EventId = "event-1";

    private static readonly DateTime StartDate = new(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc);

    private readonly FakeTicketsRepository _tickets = new();

    private static Ticket ATicket(long id, string seat)
    {
        var ticket = new Ticket(seat, "venue-1", EventId, StartDate);
        ticket.Id = id;
        return ticket;
    }

    private Task Handle(params long[] ticketIds) =>
        new BookingCreatedDomainEventHandler(_tickets)
            .Handle(new BookingCreatedDomainEvent(ticketIds), CancellationToken.None);

    [Fact]
    public async Task Books_every_ticket_the_booking_covers()
    {
        _tickets.Seed(ATicket(7, "A1"), ATicket(9, "A2"));

        await Handle(7, 9);

        Assert.All(_tickets.Tickets, ticket => Assert.Equal(TicketStatus.Booked, ticket.Status));
    }

    /// <summary>
    /// The regression test for the original bug. Dispatch happens around a save that has already been
    /// decided, so whatever this handler changes has to be persisted by this handler.
    /// </summary>
    [Fact]
    public async Task Saves_the_tickets_it_booked()
    {
        _tickets.Seed(ATicket(7, "A1"));

        await Handle(7);

        Assert.Equal(1, _tickets.SaveCount);
    }

    [Fact]
    public async Task Leaves_tickets_the_booking_does_not_cover_alone()
    {
        _tickets.Seed(ATicket(7, "A1"), ATicket(9, "A2"));

        await Handle(7);

        Assert.Equal(TicketStatus.Booked, _tickets.Tickets.Single(t => t.Id == 7).Status);
        Assert.Equal(TicketStatus.None, _tickets.Tickets.Single(t => t.Id == 9).Status);
    }

    /// <summary>
    /// A booking that points at a ticket which no longer exists is not something to paper over: the
    /// throw rolls back the booking that raised this event rather than leaving one behind whose seats
    /// were never really taken.
    /// </summary>
    [Fact]
    public async Task Refuses_when_a_ticket_the_booking_covers_has_gone()
    {
        _tickets.Seed(ATicket(7, "A1"));

        await Assert.ThrowsAsync<BookingException>(() => Handle(7, 9));
    }

    /// <summary>
    /// Booking a seat that is already taken or cancelled is a conflict, not something to overwrite.
    /// The ticket itself refuses it; this only confirms the handler lets that surface.
    /// </summary>
    [Fact]
    public async Task Refuses_when_a_ticket_cannot_be_booked()
    {
        var cancelled = ATicket(7, "A1");
        cancelled.Cancel(eventVersion: 2);
        _tickets.Seed(cancelled);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handle(7));
        Assert.Equal(0, _tickets.SaveCount);
    }
}
