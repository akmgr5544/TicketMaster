using Events.Domain.Entities;
using Events.Domain.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Events.Mongo.Repositories;

internal class PerformerRepository : IPerformerRepository
{
    private readonly MongoDomainContext _context;
    
    public PerformerRepository(MongoDomainContext context)
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
}