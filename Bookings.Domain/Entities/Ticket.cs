using Bookings.Domain.Abstractions;
using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public sealed class Ticket : Entity, IAggregateRoot
{
    public Ticket(string seat, string venueId, string eventId, DateTime eventDate, long eventVersion = 0)
    {
        Seat = seat;
        VenueId = venueId;
        EventId = eventId;
        EventDate = eventDate;
        EventVersion = eventVersion;
        Status = TicketStatus.None;
    }

    public long Id { get; set; }
    public string VenueId { get; set; }
    public string EventId { get; set; }
    public string Seat { get; init; }
    public DateTime EventDate { get; set; }
    public TicketStatus Status { get; private set; }

    /// <summary>
    /// The version of the catalogue event this ticket was last brought in line with. Events service
    /// stamps every change it publishes with the version it produced, and this is how far this ticket
    /// has got.
    /// </summary>
    public long EventVersion { get; set; }

    /// <summary>
    /// How long after an event starts its seats remain sellable. Latecomers can still buy, but not
    /// indefinitely.
    /// </summary>
    public static readonly TimeSpan SaleGracePeriod = TimeSpan.FromHours(5);

    /// <summary>
    /// Whether this seat can be held or sold: nobody has it, it belongs to the event being asked
    /// about, and that event has not passed out of its selling window.
    /// <para>
    /// Reservation checks this before holding a seat, so an unsellable ticket is refused at the step
    /// the user is actually performing rather than accepted and then rejected at booking. The
    /// equivalent predicate in <c>GetTicketsForBookingAsync</c> is the database-side mirror of this —
    /// a query cannot call into the domain, so if the rule changes, both have to change together.
    /// </para>
    /// </summary>
    public bool IsAvailableFor(string eventId, DateTime utcNow) =>
        Status == TicketStatus.None
        && EventId == eventId
        && EventDate > utcNow - SaleGracePeriod;

    /// <summary>
    /// True when an incoming change is not newer than what has already been applied. Delivery is
    /// at-least-once and unordered, so without this a redelivered older change silently overwrites a
    /// newer one — and equal versions are stale too, because that is the same message arriving twice.
    /// </summary>
    public bool IsStale(long eventVersion) => eventVersion <= EventVersion;

    // Each of these guards itself rather than trusting the caller to check IsStale first, so a new
    // consumer cannot introduce the bug by forgetting.

    public void Reschedule(DateTime eventDate, long eventVersion)
    {
        if (IsStale(eventVersion))
            return;

        EventDate = eventDate;
        EventVersion = eventVersion;
    }

    public void Relocate(string venueId, long eventVersion)
    {
        if (IsStale(eventVersion))
            return;

        VenueId = venueId;
        EventVersion = eventVersion;
    }

    /// <summary>
    /// The reservation converting into a durable booking. Unlike the methods above this is not a
    /// catalogue change, so it deliberately leaves <see cref="EventVersion"/> alone — moving it would
    /// make the next genuine reschedule look stale and be discarded.
    /// </summary>
    public void Book()
    {
        if (Status != TicketStatus.None)
            throw new InvalidOperationException(
                $"Ticket {Id} cannot be booked because it is already {Status}.");

        Status = TicketStatus.Booked;
    }

    /// <summary>
    /// Puts a booked seat back on sale, which is what has to happen when the payment for it never
    /// arrives. Like <see cref="Book"/> this is not a catalogue change, so the version stays put.
    /// <para>
    /// A ticket cancelled because the event itself was called off is left alone rather than refused:
    /// its holder has already been told it is void and the seat may not even exist any more, but the
    /// booking that pointed at it still has to be cancellable.
    /// </para>
    /// </summary>
    public void Release()
    {
        if (Status != TicketStatus.Booked)
            return;

        Status = TicketStatus.None;
    }

    public void Cancel(long eventVersion)
    {
        if (IsStale(eventVersion))
            return;

        Status = TicketStatus.Cancelled;
        EventVersion = eventVersion;
    }
}
