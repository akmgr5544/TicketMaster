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

    /// <summary>
    /// Called by whatever dispatches the events, immediately before publishing them. An aggregate
    /// that keeps its events past dispatch republishes them on the next save in the same context —
    /// and because a handler may itself save, that is recursion rather than a duplicate delivery.
    /// </summary>
    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    public ImmutableArray<DomainEvent> DomainEvents => [.._domainEvents];
}
