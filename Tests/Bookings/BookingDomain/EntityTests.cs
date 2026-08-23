using Bookings.Domain.Abstractions;

namespace BookingDomain;

/// <summary>
/// <c>ClearDomainEvents</c> looks like housekeeping but it is load-bearing. Domain events are
/// dispatched around a save, and a handler that saves work of its own re-enters that dispatch. An
/// aggregate that still holds its events at that point republishes them, and the handler that
/// triggered the save runs again — unbounded recursion, not a duplicate delivery.
/// </summary>
public class EntityTests
{
    private sealed class TestEntity : Entity;

    private sealed record TestEvent : DomainEvent;

    [Fact]
    public void Exposes_the_events_that_were_raised_on_it()
    {
        var entity = new TestEntity();
        entity.AddDomainEvent(new TestEvent());

        Assert.Single(entity.DomainEvents);
    }

    [Fact]
    public void Holds_nothing_once_its_events_are_cleared()
    {
        var entity = new TestEntity();
        entity.AddDomainEvent(new TestEvent());

        entity.ClearDomainEvents();

        Assert.Empty(entity.DomainEvents);
    }

    /// <summary>
    /// Callers snapshot the events before dispatching them, so clearing must not disturb a snapshot
    /// already taken — otherwise the dispatch loop would be iterating something being emptied.
    /// </summary>
    [Fact]
    public void Clearing_leaves_an_already_taken_snapshot_intact()
    {
        var entity = new TestEntity();
        entity.AddDomainEvent(new TestEvent());
        var snapshot = entity.DomainEvents;

        entity.ClearDomainEvents();

        Assert.Single(snapshot);
    }
}
