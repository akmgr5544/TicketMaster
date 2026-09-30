using System.Collections.Immutable;

namespace PaymentSystem.Domain.Abstractions;

public abstract class Entity
{
    private readonly List<DomainEvent> _domainEvents = [];

    protected void AddDomainEvent(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    // Cleared by the dispatcher immediately before publishing. Kept events would re-publish on the next
    // save in the same context — and because a handler may itself save, that is recursion.
    public void ClearDomainEvents() => _domainEvents.Clear();

    public ImmutableArray<DomainEvent> DomainEvents => [.._domainEvents];
}
