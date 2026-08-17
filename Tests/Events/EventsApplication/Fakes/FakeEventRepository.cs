using Events.Domain.Entities;
using Events.Domain.Repositories;

namespace EventsApplication.Fakes;

internal sealed class FakeEventRepository : IEventRepository
{
    private readonly List<Event> _events = [];

    public int UpcomingEventCount { get; set; }

    public IReadOnlyList<Event> Added => _events;

    public Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_events.FirstOrDefault(e => e.Id == id));

    public Task AddEventAsync(Event @event, CancellationToken cancellationToken)
    {
        _events.Add(@event);
        return Task.CompletedTask;
    }

    public Task<int> CountUpcomingEventsAtVenueAsync(string venueId, DateTime asOf, CancellationToken cancellationToken) =>
        Task.FromResult(UpcomingEventCount);
}
