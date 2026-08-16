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

    public async Task AddEventAsync(Event @event, CancellationToken cancellationToken)
    {
        await _context.Events.CreateItemAsync(@event,
            new PartitionKey(@event.Id),
            cancellationToken: cancellationToken);
    }
}
