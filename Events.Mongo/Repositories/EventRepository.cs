using Events.Domain.Entities;
using Events.Domain.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Events.Mongo.Repositories;

public class EventRepository : IEventRepository
{
    private readonly MongoDomainContext _context;
    
    public EventRepository(MongoDomainContext context)
    {
        _context = context;
    }
    
    public async ValueTask<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken)
    {
        var filter = Builders<Performer>.Filter.Eq(x=> x.Id, new ObjectId(id));
        
        var performer = await _context.Performers.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return performer;
    }

    public Task<List<Performer>> GetPerformersByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var performerIds = ids.Select(x=> new ObjectId(x));
        var filter = Builders<Performer>.Filter.In(x=> x.Id, performerIds);
        
        return _context.Performers.Find(filter).ToListAsync(cancellationToken);
    }

    public async ValueTask<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken)
    {
        var filter = Builders<Venue>.Filter.Eq(x=> x.Id, new ObjectId(id));
        var venue = await _context.Venues.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return venue;
    }

    public Task AddEventAsync(Event @event, CancellationToken cancellationToken)
    {
        return _context.Events.InsertOneAsync(@event, options: null, cancellationToken);
    }

    public Task UpdateEventAsync(Event @event, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}