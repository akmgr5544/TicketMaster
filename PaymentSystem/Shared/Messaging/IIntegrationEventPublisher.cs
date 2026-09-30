namespace PaymentSystem.Shared.Messaging;

// Joins the caller's open transaction, so the message commits or rolls back with the write it describes.
// With no transaction open it is its own unit of work — still persisted before it is sent.
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken) where TEvent : class;

    // Same transaction rules as PublishAsync; the message is stored now and delivered once the delay has passed,
    // surviving a restart in between.
    Task ScheduleAsync<TMessage>(TMessage message, TimeSpan delay, CancellationToken cancellationToken)
        where TMessage : class;
}
