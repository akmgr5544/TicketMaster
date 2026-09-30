namespace Bookings.Domain.Abstractions;

/// <summary>
/// Stages an integration event in the transaction already open on the booking's context, so the message
/// commits or rolls back with the write it describes and is sent only once that commit has happened.
/// <para>
/// Here rather than in the application layer for the reason <see cref="IAfterCommitQueue"/> is: the
/// implementation needs the DbContext, which is infrastructure, and infrastructure cannot see the
/// application project that references it.
/// </para>
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken) where TEvent : class;
}
