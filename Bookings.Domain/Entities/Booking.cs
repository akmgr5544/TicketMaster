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

    private readonly List<BookingRefund> _refunds = [];
    public IReadOnlyCollection<BookingRefund> Refunds => _refunds.AsReadOnly();

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
        AddDomainEvent(new BookingCancelledDomainEvent(Id,
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

    // The whole booking is voided, so it asks for everything still paid and covers every seat no refund covers yet.
    private void FlagForRefund()
    {
        Status = BookingStatus.RefundPending;
        BookingHistories.Add(new BookingHistory(Status, BookedTickets.Count));
        var refund = new BookingRefund(amount: null, currency: null);
        _refunds.Add(refund);
        foreach (var bookedTicket in BookedTickets.Where(bookedTicket => bookedTicket.RefundId is null))
            bookedTicket.CoverBy(refund.Id);
        AddDomainEvent(new BookingRefundRequestedDomainEvent(Id, refund.Id, Amount: null, Currency: null));
    }

    // A customer giving back some of a paid booking's seats. The booking stays paid and the seats stay held until
    // the money lands, so a refund that fails has not already sold them to somebody else.
    public Guid RequestRefund(long[] ticketIds, decimal amount, string currency)
    {
        if (Status != BookingStatus.Payed)
            throw new BookingsDomainException($"A {Status} booking cannot be refunded seat by seat.");
        if (ticketIds.Length == 0)
            throw new BookingsDomainException("Choose the seats to cancel.");
        if (ticketIds.Distinct().Count() != ticketIds.Length)
            throw new BookingsDomainException("The same seat was chosen more than once.");
        if (amount <= 0)
            throw new BookingsDomainException("A refund must give something back.");

        var seats = ticketIds
            .Select(ticketId => BookedTickets.Find(bookedTicket => bookedTicket.TicketId == ticketId)
                                ?? throw new BookingsDomainException($"Booking {Id} does not hold ticket {ticketId}."))
            .ToArray();
        if (seats.Any(seat => seat.RefundId is not null))
            throw new BookingsDomainException("A refund has already been asked for some of those seats.");

        var refund = new BookingRefund(amount, currency);
        _refunds.Add(refund);
        foreach (var seat in seats)
            seat.CoverBy(refund.Id);
        AddDomainEvent(new BookingRefundRequestedDomainEvent(Id, refund.Id, amount, currency));
        return refund.Id;
    }

    public long[] TicketsCoveredBy(Guid refundId) =>
        BookedTickets.Where(bookedTicket => bookedTicket.RefundId == refundId)
            .Select(bookedTicket => bookedTicket.TicketId)
            .ToArray();

    // The money for a refund is back, so the seats it covers go back on sale. The whole-booking refund gave back
    // everything still paid, any pending part's money with it, so it settles those parts too. A seat an earlier
    // refund already released is never released again: it may since be somebody else's.
    // No refund id: a message from before refunds had ids, which only ever answered a whole-booking refund.
    // A Cancelled booking is the other one a refund reaches: it was cancelled before its payment's success was
    // heard, the seats are already back on sale, and only the money had to follow.
    public void MarkRefunded(Guid? refundId = null)
    {
        if (Status is BookingStatus.Refunded or BookingStatus.Cancelled)
            return;

        // Null: this refund landed before, so its seats are already back.
        if ((refundId is { } id ? SettleRefund(id) : SettleUnnamedRefund()) is not { } settled)
            return;

        // An unnamed refund answered the whole booking, so a seat no refund covered goes back with it.
        var released = BookedTickets
            .Where(bookedTicket => bookedTicket.RefundId is { } covering ? settled.Contains(covering) : refundId is null)
            .Select(bookedTicket => bookedTicket.TicketId)
            .ToArray();
        if (released.Length > 0)
            AddDomainEvent(new BookingRefundedDomainEvent(Id, released));

        if (refundId is null || BookedTickets.TrueForAll(IsRefunded))
        {
            Status = BookingStatus.Refunded;
            BookingHistories.Add(new BookingHistory(Status, BookedTickets.Count));
        }
    }

    private HashSet<Guid>? SettleRefund(Guid refundId)
    {
        var refund = _refunds.Find(candidate => candidate.Id == refundId)
                     ?? throw new BookingsDomainException($"Booking {Id} never asked for refund {refundId}.");
        if (!refund.Complete())
            return null;

        var settled = new HashSet<Guid> { refund.Id };
        if (refund.IsWholeBooking)
            settled.UnionWith(SettlePending());
        return settled;
    }

    private HashSet<Guid> SettleUnnamedRefund()
    {
        if (Status != BookingStatus.RefundPending)
            throw new BookingsDomainException($"A {Status} booking was not waiting for a refund.");

        return SettlePending().ToHashSet();
    }

    // Materialised at once: Complete changes state, so a lazily re-enumerated query would find nothing pending.
    private List<Guid> SettlePending() =>
        _refunds.Where(pending => pending.Complete()).Select(pending => pending.Id).ToList();

    private bool IsRefunded(BookedTicket bookedTicket) =>
        bookedTicket.RefundId is { } id
        && _refunds.Exists(refund => refund.Id == id && refund.Status == BookingRefundStatus.Completed);

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