namespace Events.Domain.Abstractions;

/// <summary>
/// An aggregate root that records what happened to it.
/// <para>
/// <see cref="DomainEvents"/> is in-memory bookkeeping, not part of the aggregate's persisted
/// state. Because there is no JSON attribute anywhere in this project, keeping it out of the stored
/// document is <c>DomainBinding</c>'s job in Events.Cosmos — see the note there.
/// </para>
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Called once the events have been handed to the publisher, so a second save cannot publish
    /// them again.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
