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

    public async Task AddPerformerAsync(Performer performer, CancellationToken cancellationToken)
    {
        await _context.Performers.CreateItemAsync(performer,
            new PartitionKey(performer.Id),
            cancellationToken: cancellationToken);
    }
}
