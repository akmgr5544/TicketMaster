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
    public TicketStatus Status { get; set; }

    /// <summary>
    /// The version of the catalogue event this ticket was last brought in line with. Events service
    /// stamps every change it publishes with the version it produced, and this is how far this ticket
    /// has got.
    /// </summary>
    public long EventVersion { get; set; }

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

    public void Cancel(long eventVersion)
    {
        if (IsStale(eventVersion))
            return;

        Status = TicketStatus.Cancelled;
        EventVersion = eventVersion;
    }
}
