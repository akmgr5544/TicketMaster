using System.Net;
using System.Net.Http.Json;
using EventsIntegration.Fixtures;

namespace EventsIntegration.HostFixtures;

// Over real HTTP, because the failure this guards is in model binding: the route names the resource, so a body that
// leaves the id out is the ordinary request and must not be refused before the handler runs.
[Collection(EventsHostCollection.Name)]
public sealed class UpdateEndpointTests
{
    private readonly EventsHostFixture _fixture;

    public UpdateEndpointTests(EventsHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_venue_updates_without_repeating_the_id_in_the_body()
    {
        var venue = await new Seed(_fixture.Services).VenueAsync();
        using var client = _fixture.CreateClient();

        using var response = await client.PutAsJsonAsync($"api/venues/{venue.Id}",
            new { name = "Renamed Arena", address = "2 Main St", latitude = 41.0, longitude = -71.0 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_performer_updates_without_repeating_the_id_in_the_body()
    {
        var performer = await new Seed(_fixture.Services).PerformerAsync();
        using var client = _fixture.CreateClient();

        using var response = await client.PutAsJsonAsync($"api/performers/{performer.Id}",
            new { name = "Renamed Band", description = "support act" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
