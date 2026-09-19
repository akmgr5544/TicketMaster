using Events.Application.Commands;
using Events.Application.Exceptions;
using EventsIntegration.Fixtures;
using MediatR;

namespace EventsIntegration.DeleteGuards;

// The venue and performer delete guards each run a cross-partition query — a COUNT over c.venue.id
// and an EXISTS over c.performers — and refuse the delete when an upcoming event still needs the
// row. Their shape had been reviewed and never executed; these run them against live data. Driven
// through ISender so the handler, the query and the mapping all run as production wires them.
public sealed class DeleteGuardTests : EventsIntegrationTest
{
    public DeleteGuardTests(EventsFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_venue_with_an_upcoming_event_cannot_be_deleted()
    {
        var venue = await Seed.VenueAsync();
        await Seed.EventAsync(venue);

        var refusal = await Assert.ThrowsAsync<EventsApplicationException>(
            () => Sender.Send(new DeleteVenueCommand(venue.Id)));

        Assert.Contains("upcoming event", refusal.Message);
        Assert.NotNull(await Venues.GetVenueByIdAsync(venue.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_venue_with_no_upcoming_events_is_deleted()
    {
        var venue = await Seed.VenueAsync();

        await Sender.Send(new DeleteVenueCommand(venue.Id));

        Assert.Null(await Venues.GetVenueByIdAsync(venue.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_performer_in_an_upcoming_lineup_cannot_be_deleted()
    {
        var performer = await Seed.PerformerAsync();
        await Seed.EventAsync(performer: performer);

        var refusal = await Assert.ThrowsAsync<EventsApplicationException>(
            () => Sender.Send(new DeletePerformerCommand(performer.Id)));

        Assert.Contains("upcoming event", refusal.Message);
        Assert.NotNull(await Performers.GetPerformerByIdAsync(performer.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_performer_in_no_lineup_is_deleted()
    {
        var performer = await Seed.PerformerAsync();

        await Sender.Send(new DeletePerformerCommand(performer.Id));

        Assert.Null(await Performers.GetPerformerByIdAsync(performer.Id, CancellationToken.None));
    }
}
