using Events.Domain.Abstractions;

namespace Events.Domain.DomainEvents;

/// <summary>
/// What happened to an <c>Event</c>. Every one of these carries the aggregate's
/// <c>Version</c> at the moment it happened, because consumers outside this service use it to
/// discard messages that arrive out of order.
/// <para>
/// Each event carries the *resulting state* of what it changed rather than a delta — a relocation
/// says which seats the event now has, not which seats were added and removed. That is what lets a
/// consumer apply the same message twice and land in the same place.
/// </para>
/// </summary>
public record EventCreatedDomainEvent(string EventId,
    long Version,
    string VenueId,
    DateTime StartDate,
    IReadOnlyList<string> Seats) : IDomainEvent;

public record EventRescheduledDomainEvent(string EventId,
    long Version,
    DateTime StartDate) : IDomainEvent;

public record EventRelocatedDomainEvent(string EventId,
    long Version,
    string VenueId,
    DateTime StartDate,
    IReadOnlyList<string> Seats) : IDomainEvent;

/// <summary>
/// Raised for completeness of the aggregate's history. It has no integration contract, because
/// nothing outside this service depends on who is performing — tickets key off venue, seat and
/// date. Adding a contract later is purely additive.
/// </summary>
public record EventLineupChangedDomainEvent(string EventId,
    long Version,
    IReadOnlyList<string> PerformerIds) : IDomainEvent;

public record EventCancelledDomainEvent(string EventId, long Version) : IDomainEvent;
