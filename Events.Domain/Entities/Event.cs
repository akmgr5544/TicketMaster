namespace Events.Domain.Entities;

public class Event
{
    public Event(long venueId,DateTime startDate, Venue venue, List<Performer> performers)
    {
        VenueId = venueId;
        Venue = venue;
        Performers = performers;
        StartDate = startDate;
    }
    public string Id { get; set; }
    public long VenueId { get; set; }
    public DateTime StartDate { get; set; }
    public Venue Venue { get; set; }
    public List<Performer> Performers { get; set; }
}