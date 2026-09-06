using Events.Domain.Entities;
using Events.Domain.Repositories;
using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Repositories;

internal class PerformerRepository : IPerformerRepository
{
    private readonly EventsCosmosContext _context;

    // Scoped alongside this repository: see ETagCache for why that lifetime is load-bearing.
    private readonly ETagCache _etags = new();

    public PerformerRepository(EventsCosmosContext context)
    {
        _context = context;
    }

    public async Task<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken)
    {
        var (performer, etag) = await _context.Performers.PointReadWithETagAsync<Performer>(id, cancellationToken);

        _etags.Record(id, etag);

        return performer;
    }

    public async Task<IReadOnlyList<Performer>> GetPerformersByIdsAsync(IEnumerable<string> ids,
        CancellationToken cancellationToken)
    {
        var lookups = ids.Distinct()
            .Select(id => (id, new PartitionKey(id)))
            .ToList();

        if (lookups.Count == 0)
            return [];

        var performers = await _context.Performers.ReadManyItemsAsync<Performer>(lookups,
            cancellationToken: cancellationToken);

        return [..performers];
    }

    /// <summary>
    /// A cross-partition query — with <c>/id</c> partition keys every performer lives in its own
    /// logical partition, so listing necessarily fans out. Affordable at catalogue size.
    /// <para>
    /// Only the first page is read per call. The SDK's continuation token is handed back to the
    /// caller and passed in again next time, which is why paging costs the same whether the caller
    /// asks for page 2 or page 200 — unlike OFFSET, which is charged for the rows it skips.
    /// </para>
    /// </summary>
    public async Task<Page<Performer>> ListPerformersAsync(int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c ORDER BY c.name");

        using var iterator = _context.Performers.GetItemQueryIterator<Performer>(query,
            // An empty string is not a valid token; null means "start from the beginning".
            continuationToken: string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken,
            requestOptions: new QueryRequestOptions { MaxItemCount = pageSize });

        if (!iterator.HasMoreResults)
            return new Page<Performer>([], null);

        var response = await iterator.ReadNextAsync(cancellationToken);

        return new Page<Performer>([..response], response.ContinuationToken);
    }

    public async Task AddPerformerAsync(Performer performer, CancellationToken cancellationToken)
    {
        var etag = await _context.Performers.CreateAsync(performer, performer.Id, cancellationToken);

        _etags.Record(performer.Id, etag);
    }

    /// <summary>
    /// Conditional on the performer not having changed since this scope read it, so two concurrent
    /// updates cannot both write — one is refused with
    /// <see cref="Events.Domain.Exceptions.ConcurrencyConflictException"/> and retried a layer up.
    /// </summary>
    public async Task UpdatePerformerAsync(Performer performer, CancellationToken cancellationToken)
    {
        var etag = await _context.Performers.ReplaceWithETagAsync(performer,
            performer.Id,
            _etags.For(performer.Id),
            cancellationToken);

        _etags.Record(performer.Id, etag);
    }

    /// <summary>
    /// Also conditional: the caller decides whether a performer may be deleted from what it read, so
    /// a performer that changed in between must not be deleted on the strength of the old copy.
    /// </summary>
    public async Task DeletePerformerAsync(string id, CancellationToken cancellationToken)
    {
        await _context.Performers.DeleteWithETagAsync<Performer>(id, _etags.For(id), cancellationToken);
    }
}
