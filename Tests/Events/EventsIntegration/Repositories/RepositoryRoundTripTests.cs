using Events.Domain.Enums;
using Events.Domain.Repositories;
using EventsIntegration.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EventsIntegration.Repositories;

// The serialization tests in EventsCosmos prove the document shape against CosmosJson.Options in
// isolation; these prove the same options survive a real write and read through the live SDK — the
// combination nothing exercised before. Reads happen in a fresh scope so nothing comes back off a
// tracked instance.
public sealed class RepositoryRoundTripTests : EventsIntegrationTest
{
    public RepositoryRoundTripTests(EventsFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Point_read_of_a_missing_id_returns_null()
    {
        var found = await Events.GetEventByIdAsync(Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task A_created_event_round_trips_its_full_snapshot()
    {
        var venue = await Seed.VenueAsync("The Forum", ["S1", "S2", "S3"]);
        var performer = await Seed.PerformerAsync("Headliner");
        var seeded = await Seed.EventAsync(venue, performer);

        var loaded = await InScopeAsync(sp =>
            sp.GetRequiredService<IEventRepository>().GetEventByIdAsync(seeded.Id, CancellationToken.None));

        Assert.NotNull(loaded);
        Assert.Equal(seeded.Id, loaded!.Id);
        Assert.Equal(EventStatus.Scheduled, loaded.Status);
        Assert.Equal(1, loaded.Version);

        // The embedded venue snapshot, including the GeoLocation that goes through GeoLocationConverter.
        Assert.Equal("The Forum", loaded.Venue.Name);
        Assert.Equal(40, loaded.Venue.Location.Latitude);
        Assert.Equal(-70, loaded.Venue.Location.Longitude);
        Assert.Equal(["S1", "S2", "S3"], loaded.Venue.Seats);

        // The embedded performer snapshot.
        Assert.Equal(performer.Id, Assert.Single(loaded.Performers).Id);
    }

    // No continuation-token paging test lives here on purpose: the vnext emulator does not honor
    // QueryRequestOptions.MaxItemCount the way real Cosmos does — a pageSize of 2 came back with all
    // three items in one page — so a paging test here would assert the emulator's behavior, not the
    // repository's. Left to the manual check against a real account.
}
