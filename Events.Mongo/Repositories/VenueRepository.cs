using System.Drawing;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Events.Mongo.Repositories;

internal class VenueRepository : IVenueRepository
{
    private readonly MongoDomainContext _context;

    public VenueRepository(MongoDomainContext context)
    {
        _context = context;
    }

    public async ValueTask<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken)
    {
        var filter = Builders<Venue>.Filter.Eq(x => x.Id, new ObjectId(id));
        var venue = await _context.Venues.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return venue;
    }

    public Task AddVenueAsync(Venue venue, CancellationToken cancellationToken)
    {
        return _context.Venues.InsertOneAsync(venue, cancellationToken: cancellationToken);
    }

    public Task UpdateVenueAsync(string id,
        string name,
        string address,
        Point location,
        CancellationToken cancellationToken)
    {
        var filter = Builders<Venue>.Filter.Eq(x => x.Id, new ObjectId(id));
        var update = Builders<Venue>.Update
            .Set(v => v.Name, name) // Set a specific field to a new value
            .Set(v => v.Address, address)
            .Set(v => v.Location, location);

        return _context.Venues.UpdateOneAsync(filter, update, null, cancellationToken);
    }

    public Task DeleteVenueAsync(string id, CancellationToken cancellationToken)
    {
        var filter = Builders<Venue>.Filter.Eq(x => x.Id, new ObjectId(id));
        return _context.Venues.DeleteOneAsync(filter, cancellationToken);
    }
}