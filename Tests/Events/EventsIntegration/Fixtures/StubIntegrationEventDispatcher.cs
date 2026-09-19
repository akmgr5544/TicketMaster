using Events.Application.IntegrationEvents;

namespace EventsIntegration.Fixtures;

// Replaces CosmosOutboxDispatcher, the one hop that needs a live Wolverine runtime and a broker.
// Everything above it stays real: OutboxIntegrationEventPublisher still translates the aggregate's
// domain events through IntegrationEventTranslator and hands the results here. The direct analogue of
// BookingIntegration's StubEventsService — replace the last outbound hop, keep the wiring that leads
// to it. Proving the outbox relay itself is a separate, still-open item.
internal sealed class StubIntegrationEventDispatcher : IIntegrationEventDispatcher
{
    private readonly List<object> _dispatched = [];

    public IReadOnlyList<object> Dispatched => _dispatched;

    public Task DispatchAsync(IReadOnlyCollection<object> integrationEvents, CancellationToken cancellationToken)
    {
        _dispatched.AddRange(integrationEvents);
        return Task.CompletedTask;
    }
}
