using MongoDB.Bson;

namespace Events.Domain.Entities;

public class Event
{
    public Event(DateTime startDate, Venue venue, List<Performer> performers)
    {
        Venue = venue;
        Performers = performers;
        StartDate = startDate;
    }
    public ObjectId Id { get; set; }
    public DateTime StartDate { get; set; }
    public Venue Venue { get; set; }
    public List<Performer> Performers { get; set; }
}