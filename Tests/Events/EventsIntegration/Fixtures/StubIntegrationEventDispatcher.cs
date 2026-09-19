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

    // The stub is a collection-lifetime singleton so events dispatched in a test's act scope stay
    // readable after that scope disposes. That also means it accumulates across tests, so the fixture
    // clears it on reset — the Cosmos analogue of flushing state between tests.
    public void Clear() => _dispatched.Clear();

    public Task DispatchAsync(IReadOnlyCollection<object> integrationEvents, CancellationToken cancellationToken)
    {
        _dispatched.AddRange(integrationEvents);
        return Task.CompletedTask;
    }
}
