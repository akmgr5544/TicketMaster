using Events.Application.Commands;
using EventsIntegration.Fixtures;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Runtime;

namespace EventsIntegration.HostFixtures;

// The canary for the whole fixture: if the real Events host cannot start against the emulator and the
// broker — the Wolverine Cosmos persistence reaching Cosmos in Gateway mode being the risk — this is
// where it fails, named, rather than as every outbox test failing for no stated reason.
[Collection(EventsHostCollection.Name)]
public sealed class OutboxHostTests
{
    private readonly EventsHostFixture _fixture;

    public OutboxHostTests(EventsHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void The_host_starts()
    {
        Assert.NotNull(_fixture.Services.GetRequiredService<IWolverineRuntime>());
    }

    [Fact]
    public async Task A_created_event_is_relayed_through_the_outbox()
    {
        var (eventId, seats) = await CreateAnEventAsync();

        // Waiting on the probe consumer is the whole proof: the envelope was persisted to the wolverine
        // container, serialized, relayed to RabbitMQ and delivered — none of which anything exercised
        // before. A generous timeout because the durable relay polls rather than sends inline.
        var relayed = await _fixture.Sink.WaitForNextAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(eventId, relayed.EventId);
        Assert.Equal(seats, relayed.Seats);
    }

    // Seeds a venue and performer directly, then drives the real create-event command so the handler
    // stages EventCreated through the production outbox seam. Returns what the relayed message must
    // carry so the assertions do not restate the seed.
    private async Task<(string EventId, string[] Seats)> CreateAnEventAsync()
    {
        var seed = new Seed(_fixture.Services);
        var venue = await seed.VenueAsync();
        var performer = await seed.PerformerAsync();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var eventId = await sender.Send(new CreateEventCommand(DateTime.UtcNow.AddDays(14),
            venue.Id,
            [performer.Id]));

        return (eventId, [..venue.Seats]);
    }
}
