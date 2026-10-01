using System.Net;
using System.Net.Http.Json;
using EventsIntegration.Fixtures;

namespace EventsIntegration.HostFixtures;

// Over real HTTP because the defect lived in MVC, not the handler: the document was written and only then
// did CreatedAtAction fail to build its Location, so the caller saw a 500 for a create that had happened.
[Collection(EventsHostCollection.Name)]
public sealed class CreateEndpointTests
{
    private readonly EventsHostFixture _fixture;

    public CreateEndpointTests(EventsHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Creating_a_venue_answers_201_with_a_location_that_resolves()
    {
        await AssertCreatedAsync("api/venues",
            new { name = "Created Hall", address = "1 Test St", latitude = 40.18, longitude = 44.51, seats = new[] { "A1" } });
    }

    [Fact]
    public async Task Creating_a_performer_answers_201_with_a_location_that_resolves()
    {
        await AssertCreatedAsync("api/performers", new { name = "Created Act", description = "A performer" });
    }

    [Fact]
    public async Task Creating_an_event_answers_201_with_a_location_that_resolves()
    {
        var seed = new Seed(_fixture.Services);
        var venue = await seed.VenueAsync();
        var performer = await seed.PerformerAsync();

        await AssertCreatedAsync("api/events",
            new { startDate = DateTime.UtcNow.AddDays(14), venue = venue.Id, performers = new[] { performer.Id } });
    }

    private async Task AssertCreatedAsync(string path, object body)
    {
        using var client = _fixture.CreateClient();

        using var created = await client.PostAsJsonAsync(path, body);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedBody>())!.Id;
        Assert.NotNull(created.Headers.Location);
        Assert.EndsWith($"/{path}/{id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var fetched = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    private sealed record CreatedBody(string Id);
}
