using Bookings.Domain.Abstractions;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;

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
    
    public long EventVersion { get; set; }
    
    public static readonly TimeSpan SaleGracePeriod = TimeSpan.FromHours(5);

    public static DateTime SaleWindowStart(DateTime utcNow) => utcNow - SaleGracePeriod;

    public bool IsAvailableFor(string eventId, DateTime utcNow) =>
        Status == TicketStatus.None
        && EventId == eventId
        && EventDate > SaleWindowStart(utcNow);
    
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
    
    public void Book()
    {
        if (Status != TicketStatus.None)
            throw new BookingsDomainException(
                $"Ticket {Id} cannot be booked because it is already {Status}.");

        Status = TicketStatus.Booked;
    }
    
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
