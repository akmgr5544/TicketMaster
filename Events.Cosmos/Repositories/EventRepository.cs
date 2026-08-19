using Events.Domain.Entities;
using Events.Domain.Repositories;
using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Repositories;

internal class EventRepository : IEventRepository
{
    private readonly EventsCosmosContext _context;

    public EventRepository(EventsCosmosContext context)
    {
        _context = context;
    }

    public Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken)
    {
        return _context.Events.PointReadAsync<Event>(id, cancellationToken);
    }

    /// <summary>
    /// A cross-partition query — with <c>/id</c> partition keys every event lives in its own logical
    /// partition, so listing necessarily fans out. Cursor paging, because Cosmos charges for the
    /// rows an OFFSET skips.
    /// </summary>
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
        await _context.Events.CreateItemAsync(@event,
            new PartitionKey(@event.Id),
            cancellationToken: cancellationToken);
    }

    public async Task UpdateEventAsync(Event @event, CancellationToken cancellationToken)
    {
        await _context.Events.ReplaceItemAsync(@event,
            @event.Id,
            new PartitionKey(@event.Id),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Filters on the *embedded* venue snapshot (<c>c.venue.id</c>) because that is what an event
    /// document actually carries — there is no join to the venues container.
    /// <para>
    /// Cross-partition and cross-container, so this is the most expensive read in the service. It
    /// exists only to guard venue deletion, which is rare.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Filters on the *embedded* performer snapshots because that is what an event document
    /// actually carries — there is no join to the performers container.
    /// <para>
    /// A correlated EXISTS subquery rather than <c>ARRAY_CONTAINS</c>: matching an array of objects
    /// with ARRAY_CONTAINS needs an object literal, which cannot carry a query parameter, and
    /// interpolating the id into the text instead would be string-built SQL.
    /// </para>
    /// <para>Cross-partition, and it exists only to guard performer deletion, which is rare.</para>
    /// </summary>
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
