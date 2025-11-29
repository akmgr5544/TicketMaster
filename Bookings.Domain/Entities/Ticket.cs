namespace Bookings.Domain.Entities;

public class Ticket : RootEntity
{
    public Ticket(string seats, int venueId, int eventId,  DateTime eventDate)
    {
        Seats = seats;
        VenueId = venueId;
        EventId = eventId;
        EventDate = eventDate;
    }

    public long Id { get; set; }
    public long VenueId { get; set; }
    public long EventId { get; set; }
    public string Seats { get; init; }
    public DateTime EventDate { get; set; }
}