using Events.Application.IntegrationEvents;
using Wolverine.CosmosDb;
using Wolverine.Runtime;

namespace Events.Cosmos.IntegrationEvents;

internal sealed class CosmosOutboxDispatcher : IIntegrationEventDispatcher
{
    private readonly IWolverineRuntime _runtime;
    private readonly EventsCosmosContext _context;

    public CosmosOutboxDispatcher(IWolverineRuntime runtime, EventsCosmosContext context)
    {
        _runtime = runtime;
        _context = context;
    }

    public async Task DispatchAsync(IReadOnlyCollection<object> integrationEvents,
        CancellationToken cancellationToken)
    {
        var outbox = new CosmosDbOutbox(_runtime, _context.Events);

        // These are events: publish (fanout), matching the previous inline behavior.
        foreach (var integrationEvent in integrationEvents)
            await outbox.PublishAsync(integrationEvent);

        await outbox.SaveChangesAsync(cancellationToken);
    }
}
