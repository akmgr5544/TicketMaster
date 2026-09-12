using Events.Application.IntegrationEvents;
using Events.Domain.Entities;
using Events.Domain.ValueObjects;
using TicketMaster.Common.IntegrationEvents;

namespace EventsApplication;

public class OutboxIntegrationEventPublisherTests
{
    /// <summary>Stands in for the Cosmos-side dispatcher so the seam is exercised without Cosmos.</summary>
    private sealed class RecordingDispatcher : IIntegrationEventDispatcher
    {
        public List<object> Dispatched { get; } = [];
        public int Calls { get; private set; }

        public Task DispatchAsync(IReadOnlyCollection<object> integrationEvents,
            CancellationToken cancellationToken)
        {
            Calls++;
            Dispatched.AddRange(integrationEvents);
            return Task.CompletedTask;
        }
    }

    private static Venue AVenue(params string[] seats) =>
        new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", new GeoLocation(40.1872, 44.5152),
            seats.Length == 0 ? ["A1"] : seats);

    private static Performer APerformer() => new("System of a Down", "Armenian-American rock band");

    private static Event AnEvent() => new(DateTime.UtcNow.AddDays(11), AVenue(), [APerformer()]);

    [Fact]
    public async Task Stages_translated_contracts_and_clears_the_aggregate()
    {
        var dispatcher = new RecordingDispatcher();
        var publisher = new OutboxIntegrationEventPublisher(dispatcher);
        var @event = AnEvent();

        await publisher.PublishPendingAsync(@event, CancellationToken.None);

        Assert.Equal(1, dispatcher.Calls);
        Assert.IsType<EventCreatedIntegrationEvent>(Assert.Single(dispatcher.Dispatched));
        Assert.Empty(@event.DomainEvents);
    }

    /// <summary>
    /// A lineup change has no public contract. The outbox must not be touched for an empty batch, but
    /// the aggregate must still be cleared so the pending domain event cannot re-publish later.
    /// </summary>
    [Fact]
    public async Task Skips_the_outbox_when_nothing_translates_but_still_clears()
    {
        var dispatcher = new RecordingDispatcher();
        var publisher = new OutboxIntegrationEventPublisher(dispatcher);
        var @event = AnEvent();
        @event.ClearDomainEvents();
        @event.ChangeLineup([APerformer()]);

        await publisher.PublishPendingAsync(@event, CancellationToken.None);

        Assert.Equal(0, dispatcher.Calls);
        Assert.Empty(dispatcher.Dispatched);
        Assert.Empty(@event.DomainEvents);
    }
}
