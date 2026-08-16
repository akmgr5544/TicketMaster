using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IPerformerRepository
{
    Task<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Performer>> GetPerformersByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken);
    Task AddPerformerAsync(Performer performer, CancellationToken cancellationToken);
}
