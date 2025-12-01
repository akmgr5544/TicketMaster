using Events.Domain.Entities;
using MongoDB.Driver;

namespace Events.Mongo;

public class MongoDomainContext
{
    public IMongoCollection<Event> Events { get; init; }
    public IMongoCollection<Performer> Performers { get; init; }
    public IMongoCollection<Venue> Venues { get; init; }

    public MongoDomainContext(IMongoDatabase mongoDatabase)
    {
        //TODO:: pass config to remove hard code
        Events = mongoDatabase.GetCollection<Event>("events");
        Performers = mongoDatabase.GetCollection<Performer>("performers");
        Venues = mongoDatabase.GetCollection<Venue>("venues");
    }
}