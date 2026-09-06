using Events.Domain.Entities;
using Events.Domain.Repositories;
using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Repositories;

internal class VenueRepository : IVenueRepository
{
    private readonly EventsCosmosContext _context;

    private readonly ETagCache _etags = new();

    public VenueRepository(EventsCosmosContext context)
    {
        _context = context;
    }

    public async Task<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken)
    {
        var (venue, etag) = await _context.Venues.PointReadWithETagAsync<Venue>(id, cancellationToken);

        _etags.Record(id, etag);

        return venue;
    }

    public async Task<Page<Venue>> ListVenuesAsync(int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name");

        using var iterator = _context.Venues.GetItemQueryIterator<Venue>(query,
            // An empty string is not a valid token; null means "start from the beginning".
            continuationToken: string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken,
            requestOptions: new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
            return new Page<Venue>([], null);

        var response = await iterator.ReadNextAsync(cancellationToken);

        return new Page<Venue>([..response], response.ContinuationToken);
    }

    public async Task AddVenueAsync(Venue venue, CancellationToken cancellationToken)
    {
        var etag = await _context.Venues.CreateAsync(venue, venue.Id, cancellationToken);

        _etags.Record(venue.Id, etag);
    }

    public async Task UpdateVenueAsync(Venue venue, CancellationToken cancellationToken)
    {
        var etag = await _context.Venues.ReplaceWithETagAsync(venue,
            venue.Id,
            _etags.For(venue.Id),
            cancellationToken);

        _etags.Record(venue.Id, etag);
    }

    public async Task DeleteVenueAsync(string id, CancellationToken cancellationToken)
    {
        await _context.Venues.DeleteWithETagAsync<Venue>(id, _etags.For(id), cancellationToken);
    }
}
