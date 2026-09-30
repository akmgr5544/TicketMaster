using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Messaging;

namespace PaymentIntegration.Fixtures;

// TransactionId is the transaction open on the publishing scope's context at the moment of publishing, so
// a test can assert a message was staged inside the unit of work that wrote the state it describes.
public sealed record PublishedIntegrationEvent(object Event, Guid? TransactionId);

public sealed record ScheduledMessage(object Message, TimeSpan Delay, Guid? TransactionId);

public sealed class IntegrationEventLog
{
    private readonly Lock _gate = new();
    private readonly List<PublishedIntegrationEvent> _published = [];
    private readonly List<ScheduledMessage> _scheduled = [];

    public IReadOnlyList<PublishedIntegrationEvent> Published
    {
        get { lock (_gate) return [.. _published]; }
    }

    // Kept apart from Published: a timer is not an outcome, and "nothing was published" should not count it.
    public IReadOnlyList<ScheduledMessage> Scheduled
    {
        get { lock (_gate) return [.. _scheduled]; }
    }

    public IReadOnlyList<T> OfType<T>() => Published.Select(p => p.Event).OfType<T>().ToArray();

    internal void Add(PublishedIntegrationEvent published)
    {
        lock (_gate) _published.Add(published);
    }

    internal void Add(ScheduledMessage scheduled)
    {
        lock (_gate) _scheduled.Add(scheduled);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _published.Clear();
            _scheduled.Clear();
        }
    }
}

// Stands in for the Wolverine outbox publisher, which needs a running Wolverine host. PaymentsHostFixture
// covers the real one.
internal sealed class RecordingIntegrationEventPublisher(IntegrationEventLog log, PaymentDbContext context)
    : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken) where TEvent : class
    {
        log.Add(new PublishedIntegrationEvent(integrationEvent, context.Database.CurrentTransaction?.TransactionId));
        return Task.CompletedTask;
    }

    public Task ScheduleAsync<TMessage>(TMessage message, TimeSpan delay, CancellationToken cancellationToken)
        where TMessage : class
    {
        log.Add(new ScheduledMessage(message, delay, context.Database.CurrentTransaction?.TransactionId));
        return Task.CompletedTask;
    }
}
