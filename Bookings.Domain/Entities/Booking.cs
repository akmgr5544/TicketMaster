using Bookings.Domain.Abstractions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;

namespace Bookings.Domain.Entities;

public sealed class Booking : Entity, IAggregateRoot
{
    public long Id { get; init; }
    public string UserId { get; init; } = null!;
    public BookingStatus Status { get; private set; }
    public List<BookingHistory> BookingHistories { get; init; }
    public List<BookedTicket> BookedTickets { get; init; }

    private Booking()
    {

        BookedTickets = [];
        BookingHistories = [];
    }

    private Booking(string userId,
        BookingStatus status) : this()
    {
        UserId = userId;
        Status = status;
    }

    /// <summary>
    /// Private because a booking's tickets, its history entry and its creation event have to be
    /// established together — a caller that could add a ticket on its own would leave the count in
    /// the history wrong and the event describing a set that no longer matches.
    /// </summary>
    private void AddBookedTicket(long bookedTicketId)
    {
        BookedTickets.Add(new BookedTicket(bookedTicketId));
    }

    /// <summary>
    /// The payment for this booking came through.
    /// <para>
    /// Applying the same success twice changes nothing, because payment results are delivered at least
    /// once. Refusing a cancelled booking is what stops a late success from claiming seats that have
    /// already gone back on sale — between the two outcomes, whichever lands first is the one that
    /// sticks.
    /// </para>
    /// </summary>
    public void MarkPaid()
    {
        if (Status == BookingStatus.Payed)
            return;

        if (Status != BookingStatus.Booked)
            throw new BookingsDomainException($"A {Status} booking cannot be paid for.");

        Status = BookingStatus.Payed;
        BookingHistories.Add(new BookingHistory(Status, BookedTickets.Count));
    }

    /// <summary>
    /// The payment failed or never came, so the booking is void and its seats go back on sale.
    /// <para>
    /// Refuses a booking that has been paid for: undoing that is a refund, which this service does not
    /// do. Applying the same failure twice announces the release only once, so the tickets are not
    /// released a second time after somebody else may already have taken them.
    /// </para>
    /// </summary>
    public void Cancel()
    {
        if (Status == BookingStatus.Cancelled)
            return;

        if (Status != BookingStatus.Booked)
            throw new BookingsDomainException($"A {Status} booking cannot be cancelled.");

        Status = BookingStatus.Cancelled;
        BookingHistories.Add(new BookingHistory(Status, BookedTickets.Count));
        AddDomainEvent(new BookingCancelledDomainEvent(
            BookedTickets.Select(bookedTicket => bookedTicket.TicketId).ToArray()));
    }

    /// <summary>
    /// Raises <see cref="BookingCreatedDomainEvent"/> itself rather than leaving the handler to
    /// assemble one afterwards, so a new caller cannot create a booking that never announces itself
    /// and leaves its tickets unbooked.
    /// </summary>
    public static Booking Create(string userId, BookingStatus status, long[] ticketIds)
    {
        if (ticketIds.Length == 0)
            throw new BookingsDomainException("A booking must cover at least one ticket.");

        var booking = new Booking(userId, status);

        foreach (var ticketId in ticketIds)
        {
            booking.AddBookedTicket(ticketId);
        }

        booking.BookingHistories.Add(new BookingHistory(booking.Status, ticketIds.Length));
        booking.AddDomainEvent(new BookingCreatedDomainEvent([..ticketIds]));
        return booking;
    }
}
