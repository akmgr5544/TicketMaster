using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IPerformerRepository
{
    ValueTask<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken);
    Task<List<Performer>> GetPerformersByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken);
    Task AddPerformerAsync(Performer performer, CancellationToken cancellationToken);
}