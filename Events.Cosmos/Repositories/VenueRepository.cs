using Events.Domain.Entities;
using Events.Domain.Repositories;
using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Repositories;

internal class VenueRepository : IVenueRepository
{
    private readonly EventsCosmosContext _context;

    public VenueRepository(EventsCosmosContext context)
    {
        _context = context;
    }

    public Task<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken)
    {
        return _context.Venues.PointReadAsync<Venue>(id, cancellationToken);
    }

    public async Task AddVenueAsync(Venue venue, CancellationToken cancellationToken)
    {
        await _context.Venues.CreateItemAsync(venue,
            new PartitionKey(venue.Id),
            cancellationToken: cancellationToken);
    }

    public async Task UpdateVenueAsync(Venue venue, CancellationToken cancellationToken)
    {
        await _context.Venues.ReplaceItemAsync(venue,
            venue.Id,
            new PartitionKey(venue.Id),
            cancellationToken: cancellationToken);
    }

    public async Task DeleteVenueAsync(string id, CancellationToken cancellationToken)
    {
        await _context.Venues.DeleteItemAsync<Venue>(id,
            new PartitionKey(id),
            cancellationToken: cancellationToken);
    }
}
