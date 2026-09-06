using Events.Domain.Entities;
using Events.Domain.Repositories;
using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Repositories;

internal class EventRepository : IEventRepository
{
    private readonly EventsCosmosContext _context;

    // Scoped alongside this repository: see ETagCache for why that lifetime is load-bearing.
    private readonly ETagCache _etags = new();

    public EventRepository(EventsCosmosContext context)
    {
        _context = context;
    }

    public async Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken)
    {
        var (@event, etag) = await _context.Events.PointReadWithETagAsync<Event>(id, cancellationToken);

        _etags.Record(id, etag);

        return @event;
    }

    public async Task<Page<Event>> ListEventsAsync(int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.startDate");

        using var iterator = _context.Events.GetItemQueryIterator<Event>(query,
            // An empty string is not a valid token; null means "start from the beginning".
            continuationToken: string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken,
            requestOptions: new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
            return new Page<Event>([], null);

        var response = await iterator.ReadNextAsync(cancellationToken);

        return new Page<Event>([..response], response.ContinuationToken);
    }

    public async Task AddEventAsync(Event @event, CancellationToken cancellationToken)
    {
        var etag = await _context.Events.CreateAsync(@event, @event.Id, cancellationToken);

        _etags.Record(@event.Id, etag);
    }

    public async Task UpdateEventAsync(Event @event, CancellationToken cancellationToken)
    {
        var etag = await _context.Events.ReplaceWithETagAsync(@event,
            @event.Id,
            _etags.For(@event.Id),
            cancellationToken);

        // The stored version has moved on; a second write of the same aggregate in this scope must
        // compare against the new ETag, not the one the original read saw.
        _etags.Record(@event.Id, etag);
    }

    public Task<int> CountUpcomingEventsAtVenueAsync(string venueId,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE c.venue.id = @venueId AND c.startDate > @asOf")
            .WithParameter("@venueId", venueId)
            // Serialized by the same options as the stored documents, so the comparison is
            // like-for-like rather than relying on two date formats happening to match.
            .WithParameter("@asOf", asOf);

        return CountAsync(query, cancellationToken);
    }

    public Task<int> CountUpcomingEventsWithPerformerAsync(string performerId,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("""
                SELECT VALUE COUNT(1) FROM c
                WHERE c.startDate > @asOf
                  AND EXISTS(SELECT VALUE p FROM p IN c.performers WHERE p.id = @performerId)
                """)
            .WithParameter("@performerId", performerId)
            .WithParameter("@asOf", asOf);

        return CountAsync(query, cancellationToken);
    }

    /// <summary>
    /// Drains a <c>COUNT(1)</c> query. The count arrives per-partition rather than as a single
    /// row, so the pages have to be summed rather than read once.
    /// </summary>
    private async Task<int> CountAsync(QueryDefinition query, CancellationToken cancellationToken)
    {
        using var iterator = _context.Events.GetItemQueryIterator<int>(query);

        var total = 0;
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            total += response.Sum();
        }

        return total;
    }
}
