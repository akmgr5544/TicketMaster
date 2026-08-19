using Events.Application.IntegrationEvents;
using Events.Domain.Abstractions;

namespace EventsApplication.Fakes;

/// <summary>
/// Records what was published and, like the real publisher, clears the aggregate afterwards — so a
/// handler that publishes twice is visible in these tests rather than only in production.
/// </summary>
internal sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly List<object> _published = [];

    public IReadOnlyList<object> Published => _published;

    public T PublishedSingle<T>() => Assert.Single(_published.OfType<T>());

    public Task PublishPendingAsync(Entity aggregate, CancellationToken cancellationToken)
    {
        _published.AddRange(IntegrationEventTranslator.Translate(aggregate.DomainEvents));
        aggregate.ClearDomainEvents();

        return Task.CompletedTask;
    }
}
