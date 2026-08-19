using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IEventRepository
{
    Task<Event?> GetEventByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Pass the continuation token from the previous page to fetch the next; null starts at the
    /// beginning. The returned token is null once there are no further pages.
    /// </summary>
    Task<Page<Event>> ListEventsAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task AddEventAsync(Event @event, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the aggregate rather than loose fields so an update cannot bypass the event's own
    /// validation — callers load it, call Reschedule/Relocate/ChangeLineup/Cancel, and save it back.
    /// </summary>
    Task UpdateEventAsync(Event @event, CancellationToken cancellationToken);

    /// <summary>
    /// How many events at this venue start after <paramref name="asOf"/>. Used to refuse deleting a
    /// venue out from under events that have not happened yet.
    /// </summary>
    Task<int> CountUpcomingEventsAtVenueAsync(string venueId, DateTime asOf, CancellationToken cancellationToken);

    /// <summary>
    /// How many events featuring this performer start after <paramref name="asOf"/>. Used to refuse
    /// deleting a performer out from under events that have not happened yet.
    /// </summary>
    Task<int> CountUpcomingEventsWithPerformerAsync(string performerId, DateTime asOf, CancellationToken cancellationToken);
}
