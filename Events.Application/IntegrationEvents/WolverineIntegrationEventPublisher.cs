using Events.Domain.Abstractions;
using Wolverine;

namespace Events.Application.IntegrationEvents;

internal sealed class WolverineIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IMessageBus _messageBus;

    public WolverineIntegrationEventPublisher(IMessageBus messageBus)
    {
        _messageBus = messageBus;
    }

    /// <summary>
    /// Publishes inline, which is the known gap: there is no outbox, so a crash after the Cosmos
    /// write and before this returns loses the message. Everything funnels through here so that
    /// adding one is a change to this method.
    /// </summary>
    public async Task PublishPendingAsync(Entity aggregate, CancellationToken cancellationToken)
    {
        foreach (var integrationEvent in IntegrationEventTranslator.Translate(aggregate.DomainEvents))
            await _messageBus.PublishAsync(integrationEvent);

        aggregate.ClearDomainEvents();
    }
}
