using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IEventRepository
{
    Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken);
    Task AddEventAsync(Event @event, CancellationToken cancellationToken);
}
