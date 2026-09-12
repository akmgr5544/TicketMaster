using Events.Domain.Abstractions;

namespace Events.Application.IntegrationEvents;

internal sealed class OutboxIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IIntegrationEventDispatcher _dispatcher;

    public OutboxIntegrationEventPublisher(IIntegrationEventDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task PublishPendingAsync(Entity aggregate, CancellationToken cancellationToken)
    {
        var integrationEvents = IntegrationEventTranslator.Translate(aggregate.DomainEvents).ToArray();
        
        if (integrationEvents.Length > 0)
            await _dispatcher.DispatchAsync(integrationEvents, cancellationToken);

        aggregate.ClearDomainEvents();
    }
}
