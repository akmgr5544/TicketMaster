using Bookings.Domain.Abstractions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;

namespace Bookings.Domain.Entities;

public sealed class Booking : Entity, IAggregateRoot
{
    public long Id { get; init; }
    public Guid UserId { get; init; }
    public DateTime CreatedAt { get; init; }
    public BookingStatus Status { get; private set; }
    public List<BookingHistory> BookingHistories { get; init; }
    public List<BookedTicket> BookedTickets { get; init; }

    private Booking()
    {
        BookedTickets = [];
        BookingHistories = [];
    }

    private Booking(Guid userId,
        BookingStatus status) : this()
    {
        UserId = userId;
        Status = status;
        // Not in the parameterless constructor: that one is EF's materialisation path, where the
        // stored value has to win. Utc because Npgsql refuses any other Kind on a timestamptz.
        CreatedAt = DateTime.UtcNow;
    }

    private void AddBookedTicket(long bookedTicketId)
    {
        BookedTickets.Add(new BookedTicket(bookedTicketId));
    }

    public void MarkPaid()
    {
        if (Status == BookingStatus.Payed)
            return;

        if (Status != BookingStatus.Booked)
            throw new BookingsDomainException($"A {Status} booking cannot be paid for.");

        Status = BookingStatus.Payed;
        BookingHistories.Add(new BookingHistory(Status, BookedTickets.Count));
    }

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

    public void OnBookedSeatCancelled()
    {
        switch (Status)
        {
            case BookingStatus.Booked:
                Cancel();
                break;
            case BookingStatus.Payed:
                FlagForRefund();
                break;
        }
    }

    private void FlagForRefund()
    {
        Status = BookingStatus.RefundPending;
        BookingHistories.Add(new BookingHistory(Status, BookedTickets.Count));
    }

    public static Booking Create(Guid userId, BookingStatus status, long[] ticketIds)
    {
        if (ticketIds.Length == 0)
            throw new BookingsDomainException("A booking must cover at least one ticket.");

        var booking = new Booking(userId, status);

        foreach (var ticketId in ticketIds)
        {
            booking.AddBookedTicket(ticketId);
        }

        booking.BookingHistories.Add(new BookingHistory(booking.Status, ticketIds.Length));
        booking.AddDomainEvent(new BookingCreatedDomainEvent([.. ticketIds]));
        return booking;
    }
}