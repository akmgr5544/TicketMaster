using Events.Domain.Entities;
using MongoDB.Driver;

namespace Events.Mongo;

public class MongoDomainContext
{
    private readonly IMongoClient _mongoClient;
    public IMongoCollection<Event> Events { get; init; }
    public IMongoCollection<Performer> Performers { get; init; }
    public IMongoCollection<Venue> Venues { get; init; }

    public MongoDomainContext(IMongoClient mongoClient)
    {
        //TODO:: pass config to remove hard code
        Events = mongoClient.GetDatabase("events").GetCollection<Event>("events");
        Performers = mongoClient.GetDatabase("events").GetCollection<Performer>("performers");
        Venues = mongoClient.GetDatabase("events").GetCollection<Venue>("venues");
        _mongoClient = mongoClient;
    }
}