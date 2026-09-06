using Events.Domain.Entities;
using Events.Domain.Exceptions;
using Events.Domain.Repositories;

namespace EventsApplication.Fakes;

internal sealed class FakeEventRepository : IEventRepository
{
    private readonly List<Event> _events = [];

    public int UpcomingEventCount { get; set; }
    public int UpcomingPerformerEventCount { get; set; }

    /// <summary>
    /// How many times the next writes should lose the race before one is allowed through — what a
    /// Cosmos ETag mismatch does, without a Cosmos.
    /// </summary>
    public int ConflictsBeforeSuccess { get; set; }

    public int GetCalls { get; private set; }

    public string? LastContinuationTokenRequested { get; private set; }
    public string? NextContinuationToken { get; set; }

    public IReadOnlyList<Event> Added => _events;

    public void Seed(params Event[] events) => _events.AddRange(events);

    public Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken)
    {
        GetCalls++;

        return Task.FromResult(_events.FirstOrDefault(e => e.Id == id));
    }

    public Task<Page<Event>> ListEventsAsync(int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        LastContinuationTokenRequested = continuationToken;

        var items = _events.Take(pageSize).ToList();
        return Task.FromResult(new Page<Event>(items, NextContinuationToken));
    }

    public Task AddEventAsync(Event @event, CancellationToken cancellationToken)
    {
        _events.Add(@event);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The stored aggregate is the same instance the handler mutated, so this only has to record
    /// that a write happened — which is what the handler tests assert on.
    /// </summary>
    public Task UpdateEventAsync(Event @event, CancellationToken cancellationToken)
    {
        if (ConflictsBeforeSuccess > 0)
        {
            ConflictsBeforeSuccess--;

            // Nothing is recorded in Updated: a refused write stored nothing, which is what makes
            // re-running the whole handler safe.
            throw new ConcurrencyConflictException(nameof(Event), @event.Id);
        }

        Updated.Add(@event);
        return Task.CompletedTask;
    }

    public List<Event> Updated { get; } = [];

    public Task<int> CountUpcomingEventsAtVenueAsync(string venueId, DateTime asOf, CancellationToken cancellationToken) =>
        Task.FromResult(UpcomingEventCount);

    public Task<int> CountUpcomingEventsWithPerformerAsync(string performerId,
        DateTime asOf,
        CancellationToken cancellationToken) =>
        Task.FromResult(UpcomingPerformerEventCount);
}
