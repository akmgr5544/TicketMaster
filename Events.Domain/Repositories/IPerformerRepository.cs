using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IPerformerRepository
{
    Task<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Performer>> GetPerformersByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Pass the continuation token from the previous page to fetch the next; null starts at the
    /// beginning. The returned token is null once there are no further pages.
    /// </summary>
    Task<Page<Performer>> ListPerformersAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task AddPerformerAsync(Performer performer, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the aggregate rather than loose fields so an update cannot bypass the performer's own
    /// validation — callers load it, call Rename/ChangeDescription, and save it back.
    /// </summary>
    Task UpdatePerformerAsync(Performer performer, CancellationToken cancellationToken);

    Task DeletePerformerAsync(string id, CancellationToken cancellationToken);
}
