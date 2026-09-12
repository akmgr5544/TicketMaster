namespace Events.Application.IntegrationEvents;

public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<object> integrationEvents, CancellationToken cancellationToken);
}
