using PaymentSystem.Domain.Abstractions;

namespace PaymentDomain;

public class EntityTests
{
    private sealed record ThingHappened : DomainEvent;

    private sealed class Thing : Entity
    {
        public void DoIt() => AddDomainEvent(new ThingHappened());
    }

    [Fact]
    public void Records_a_raised_domain_event()
    {
        var thing = new Thing();

        thing.DoIt();

        Assert.Single(thing.DomainEvents);
    }

    [Fact]
    public void Clearing_removes_pending_events()
    {
        var thing = new Thing();
        thing.DoIt();

        thing.ClearDomainEvents();

        Assert.Empty(thing.DomainEvents);
    }
}
