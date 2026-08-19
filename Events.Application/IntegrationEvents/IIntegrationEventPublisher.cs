using Events.Domain.Abstractions;

namespace Events.Application.IntegrationEvents;

/// <summary>
/// The single way anything in this service reaches the broker.
/// <para>
/// It exists as one choke point on purpose. Events has no outbox today — a crash between the Cosmos
/// write and the publish loses the message, and a consumer then never learns about a change that did
/// happen. Closing that gap means changing this one implementation rather than hunting through
/// handlers.
/// </para>
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Translates the aggregate's pending domain events into public contracts, publishes them, and
    /// clears the aggregate.
    /// <para>
    /// It takes the aggregate rather than a list of events so that clearing cannot be forgotten —
    /// a handler that publishes and then forgets to clear would re-publish everything on the next
    /// save of the same instance.
    /// </para>
    /// </summary>
    Task PublishPendingAsync(Entity aggregate, CancellationToken cancellationToken);
}
