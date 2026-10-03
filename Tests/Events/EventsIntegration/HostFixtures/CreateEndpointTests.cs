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
        using var _ = await AssertCreatedAsync("api/venues",
            new { name = "Created Hall", address = "1 Test St", latitude = 40.18, longitude = 44.51, seats = new[] { "A1" } });
    }

    [Fact]
    public async Task Creating_a_performer_answers_201_with_a_location_that_resolves()
    {
        using var _ = await AssertCreatedAsync("api/performers", new { name = "Created Act", description = "A performer" });
    }

    [Fact]
    public async Task Creating_an_event_answers_201_with_a_location_that_resolves()
    {
        using var _ = await AssertCreatedAsync("api/events", await AnEventBodyAsync(), Guid.CreateVersion7());
    }

    // The organizer is who gets paid, so it comes from the gateway's identity header and never from the body:
    // a body naming somebody else must not make them the payee.
    [Fact]
    public async Task The_organizer_is_the_caller_not_whoever_the_body_names()
    {
        var caller = Guid.CreateVersion7();
        var body = await AnEventBodyAsync(organizerId: Guid.CreateVersion7());

        using var fetched = await AssertCreatedAsync("api/events", body, caller);

        var @event = (await fetched.Content.ReadFromJsonAsync<EventBody>())!;
        Assert.Equal(caller, @event.OrganizerId);
        Assert.Equal(49.99m, @event.TicketPrice);
        Assert.Equal("USD", @event.Currency);
    }

    [Fact]
    public async Task Creating_an_event_without_an_identity_is_unauthorized()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.PostAsJsonAsync("api/events", await AnEventBodyAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<object> AnEventBodyAsync(Guid? organizerId = null)
    {
        var seed = new Seed(_fixture.Services);
        var venue = await seed.VenueAsync();
        var performer = await seed.PerformerAsync();

        var body = new Dictionary<string, object>
        {
            ["startDate"] = DateTime.UtcNow.AddDays(14),
            ["venue"] = venue.Id,
            ["performers"] = new[] { performer.Id },
            ["ticketPrice"] = 49.99m,
            ["currency"] = "usd"
        };
        if (organizerId is not null)
            body["organizerId"] = organizerId;

        return body;
    }

    private async Task<HttpResponseMessage> AssertCreatedAsync(string path, object body, Guid? userId = null)
    {
        using var client = _fixture.CreateClient();
        if (userId is not null)
            client.DefaultRequestHeaders.Add("X-Identity-UserId", userId.ToString());

        using var created = await client.PostAsJsonAsync(path, body);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedBody>())!.Id;
        Assert.NotNull(created.Headers.Location);
        Assert.EndsWith($"/{path}/{id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var fetched = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        return fetched;
    }

    private sealed record CreatedBody(string Id);

    private sealed record EventBody(Guid OrganizerId, decimal? TicketPrice, string? Currency);
}
