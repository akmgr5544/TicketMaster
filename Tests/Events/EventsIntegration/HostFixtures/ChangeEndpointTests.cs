using System.Net;
using System.Net.Http.Json;
using EventsIntegration.Fixtures;

namespace EventsIntegration.HostFixtures;

// Over real HTTP, because who is asking comes from the gateway's headers and the refusal is a status code: the
// controller, the handler and the exception mapping all have to agree.
[Collection(EventsHostCollection.Name)]
public sealed class ChangeEndpointTests
{
    private readonly EventsHostFixture _fixture;

    public ChangeEndpointTests(EventsHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_organizer_may_reprice_their_event()
    {
        var (eventId, organizer) = await AnEventAsync();

        using var response = await RepriceAsync(eventId, organizer.ToString());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Somebody_else_is_forbidden()
    {
        var (eventId, _) = await AnEventAsync();

        using var response = await RepriceAsync(eventId, Guid.CreateVersion7().ToString());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_may_change_somebody_elses_event()
    {
        var (eventId, _) = await AnEventAsync();

        using var response = await RepriceAsync(eventId, Guid.CreateVersion7().ToString(), role: "Admin");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_change_without_an_identity_is_unauthorized()
    {
        var (eventId, _) = await AnEventAsync();

        using var response = await RepriceAsync(eventId, userId: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(string EventId, Guid Organizer)> AnEventAsync()
    {
        var @event = await new Seed(_fixture.Services).EventAsync();
        return (@event.Id, @event.OrganizerId);
    }

    private async Task<HttpResponseMessage> RepriceAsync(string eventId, string? userId, string? role = null)
    {
        using var client = _fixture.CreateClient();
        if (userId is not null)
            client.DefaultRequestHeaders.Add("X-Identity-UserId", userId);
        if (role is not null)
            client.DefaultRequestHeaders.Add("X-Identity-Role", role);

        return await client.PutAsJsonAsync($"api/events/{eventId}/pricing",
            new { id = eventId, ticketPrice = 30m, currency = "USD" });
    }
}
