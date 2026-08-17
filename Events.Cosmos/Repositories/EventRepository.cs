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

    /// <summary>
    /// Filters on the *embedded* venue snapshot (<c>c.venue.id</c>) because that is what an event
    /// document actually carries — there is no join to the venues container.
    /// <para>
    /// Cross-partition and cross-container, so this is the most expensive read in the service. It
    /// exists only to guard venue deletion, which is rare.
    /// </para>
    /// </summary>
    public async Task<int> CountUpcomingEventsAtVenueAsync(string venueId,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE c.venue.id = @venueId AND c.startDate > @asOf")
            .WithParameter("@venueId", venueId)
            // Serialized by the same options as the stored documents, so the comparison is
            // like-for-like rather than relying on two date formats happening to match.
            .WithParameter("@asOf", asOf);

        using var iterator = _context.Events.GetItemQueryIterator<int>(query);

        var total = 0;
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            total += response.Sum();
        }

        return total;
    }
}
