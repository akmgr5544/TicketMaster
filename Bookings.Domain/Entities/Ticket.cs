using Bookings.Domain.Abstractions;
using Bookings.Domain.Enums;

namespace Bookings.Domain.Entities;

public sealed class Ticket : Entity, IAggregateRoot
{
    public Ticket(string seat, string venueId, string eventId,  DateTime eventDate)
    {
        Seat = seat;
        VenueId = venueId;
        EventId = eventId;
        EventDate = eventDate;
        Status = TicketStatus.None;
    }

    public long Id { get; set; }
    public string VenueId { get; set; }
    public string EventId { get; set; }
    public string Seat { get; init; }
    public DateTime EventDate { get; set; }
    public TicketStatus Status { get; set; }
}