using System.Collections.Immutable;

namespace Bookings.Domain.Abstractions;

public abstract class Entity
{
    private readonly List<DomainEvent> _domainEvents;

    protected Entity()
    {
        _domainEvents = [];
    }

    public void AddDomainEvent(DomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public ImmutableArray<DomainEvent> DomainEvents => [.._domainEvents];
}