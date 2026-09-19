using Events.Domain.Entities;
using Events.Domain.Repositories;

namespace GrpcSeam.Fixtures;

// Drives the real GetEventQueryHandler so a real exception flows through the real interceptor. Only
// GetEventByIdAsync is exercised by the lookup; the rest are never reached on this path.
internal sealed class FakeEventRepository : IEventRepository
{
    // Throws until a test arranges it, so every test states its own setup and none leans on a default.
    private Func<Event?> _onGet =
        () => throw new InvalidOperationException("FakeEventRepository behaviour was not configured.");

    public void Returns(Event @event) => _onGet = () => @event;
    public void NotFound() => _onGet = () => null;
    public void Throws(Exception exception) => _onGet = () => throw exception;

    public Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_onGet());

    public Task<Page<Event>> ListEventsAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddEventAsync(Event @event, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task UpdateEventAsync(Event @event, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> CountUpcomingEventsAtVenueAsync(string venueId, DateTime asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> CountUpcomingEventsWithPerformerAsync(string performerId, DateTime asOf, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
