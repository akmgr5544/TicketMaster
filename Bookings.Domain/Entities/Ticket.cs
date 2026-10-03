using Bookings.Domain.Abstractions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;

namespace Bookings.Domain.Entities;

public sealed class Ticket : Entity, IAggregateRoot
{
    public Ticket(string seat,
        string venueId,
        string eventId,
        DateTime eventDate,
        long eventVersion = 0,
        TicketPricing? pricing = null)
    {
        Seat = seat;
        VenueId = venueId;
        EventId = eventId;
        EventDate = eventDate;
        EventVersion = eventVersion;
        Pricing = pricing;
        Status = TicketStatus.None;
    }

    // For EF: a complex property cannot be bound through a constructor parameter.
    private Ticket()
    {
        Seat = null!;
        VenueId = null!;
        EventId = null!;
    }

    public long Id { get; set; }
    public string VenueId { get; set; }
    public string EventId { get; set; }
    public string Seat { get; init; }
    public DateTime EventDate { get; set; }
    public TicketStatus Status { get; private set; }

    public long EventVersion { get; set; }

    /// <summary>Null for a ticket created from a message sent before Events had pricing.</summary>
    public TicketPricing? Pricing { get; private set; }

    public static readonly TimeSpan SaleGracePeriod = TimeSpan.FromHours(5);

    public static DateTime SaleWindowStart(DateTime utcNow) => utcNow - SaleGracePeriod;

    // An unpriced ticket is not on sale: there is nothing to charge and nobody to pay. Refused here, so it is
    // refused at reservation rather than after a reservation has held it.
    public bool IsAvailableFor(string eventId, DateTime utcNow) =>
        Status == TicketStatus.None
        && Pricing is not null
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

    // Only a seat nobody has bought takes the new price: a booked ticket was charged what it cost then. Every ticket
    // still moves its version, so an older repricing redelivered later is refused by all of them alike.
    public void Reprice(TicketPricing pricing, long eventVersion)
    {
        if (IsStale(eventVersion))
            return;

        if (Status == TicketStatus.None)
            Pricing = pricing;

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

        var wasBooked = Status == TicketStatus.Booked;

        Status = TicketStatus.Cancelled;
        EventVersion = eventVersion;

        if (wasBooked)
            AddDomainEvent(new BookedSeatCancelledDomainEvent(Id));
    }
}