using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IEventRepository
{
    ValueTask<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken);
    Task<List<Performer>> GetPerformersByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken);
    ValueTask<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken);
    Task AddEventAsync(Event @event, CancellationToken cancellationToken);
    Task UpdateEventAsync(Event @event, CancellationToken cancellationToken);
}