using Bookings.Domain.Abstractions;
using Bookings.Sql;

namespace BookingIntegration.Fixtures;

// TransactionId is the transaction open on the publishing scope's context at the moment of publishing,
// so a test can assert a message was staged inside the unit of work that wrote the state it describes —
// which is what the real outbox relies on to drop it on rollback.
public sealed record PublishedIntegrationEvent(object Event, Guid? TransactionId);

// Scoped, like the publisher, so each test's Act scope sees only what it published — not what Seed's own
// scopes did.
public sealed class IntegrationEventLog
{
    private readonly List<PublishedIntegrationEvent> _published = [];

    public IReadOnlyList<PublishedIntegrationEvent> Published => _published;

    public IReadOnlyList<T> OfType<T>() => _published.Select(p => p.Event).OfType<T>().ToArray();

    internal void Add(PublishedIntegrationEvent published) => _published.Add(published);
}

// Stands in for the Wolverine outbox publisher, which needs a running Wolverine host. BookingsHostFixture
// covers the real one, including that a rolled-back message is never sent.
internal sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IntegrationEventLog _log;
    private readonly BookingDomainContext _context;

    public RecordingIntegrationEventPublisher(IntegrationEventLog log, BookingDomainContext context)
    {
        _log = log;
        _context = context;
    }

    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class
    {
        _log.Add(new PublishedIntegrationEvent(integrationEvent, _context.Database.CurrentTransaction?.TransactionId));
        return Task.CompletedTask;
    }
}
