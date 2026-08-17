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

    /// <summary>
    /// A cross-partition query — with <c>/id</c> partition keys every venue lives in its own
    /// logical partition, so listing necessarily fans out. Affordable at catalogue size.
    /// <para>
    /// Only the first page is read per call. The SDK's continuation token is handed back to the
    /// caller and passed in again next time, which is why paging costs the same whether the caller
    /// asks for page 2 or page 200 — unlike OFFSET, which is charged for the rows it skips.
    /// </para>
    /// </summary>
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
