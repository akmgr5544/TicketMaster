using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IEventRepository
{
    Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken);
    Task AddEventAsync(Event @event, CancellationToken cancellationToken);

    /// <summary>
    /// How many events at this venue start after <paramref name="asOf"/>. Used to refuse deleting a
    /// venue out from under events that have not happened yet.
    /// </summary>
    Task<int> CountUpcomingEventsAtVenueAsync(string venueId, DateTime asOf, CancellationToken cancellationToken);
}
