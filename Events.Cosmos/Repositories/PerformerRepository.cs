using Events.Domain.Entities;
using Events.Domain.Repositories;
using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Repositories;

internal class PerformerRepository : IPerformerRepository
{
    private readonly EventsCosmosContext _context;

    public PerformerRepository(EventsCosmosContext context)
    {
        _context = context;
    }

    public Task<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken)
    {
        return _context.Performers.PointReadAsync<Performer>(id, cancellationToken);
    }

    /// <summary>
    /// Reads many independent items by id. With <c>/id</c> as the partition key each performer sits
    /// in its own logical partition, so an IN query would fan out across all of them; ReadMany is
    /// built for exactly this and stays at point-read cost per item.
    /// </summary>
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
        await _context.Performers.CreateItemAsync(performer,
            new PartitionKey(performer.Id),
            cancellationToken: cancellationToken);
    }

    public async Task UpdatePerformerAsync(Performer performer, CancellationToken cancellationToken)
    {
        await _context.Performers.ReplaceItemAsync(performer,
            performer.Id,
            new PartitionKey(performer.Id),
            cancellationToken: cancellationToken);
    }

    public async Task DeletePerformerAsync(string id, CancellationToken cancellationToken)
    {
        await _context.Performers.DeleteItemAsync<Performer>(id,
            new PartitionKey(id),
            cancellationToken: cancellationToken);
    }
}
