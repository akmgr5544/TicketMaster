using System.Net;
using System.Net.Http.Json;
using BookingIntegration.Fixtures;

namespace BookingIntegration.Mechanics;

// Over real HTTP because the defect lived in MVC, not the handler: the booking committed and only then did
// CreatedAtAction fail to build its Location, so the caller saw a 500 for a booking that existed.
[Collection(BookingsHostCollection.Name)]
public sealed class MakeBookingEndpointTests
{
    private readonly BookingsHostFixture _fixture;
    private readonly Seed _seed;

    public MakeBookingEndpointTests(BookingsHostFixture fixture)
    {
        _fixture = fixture;
        _seed = new Seed(fixture.Services);
    }

    [Fact]
    public async Task Making_a_booking_answers_201_with_a_location_that_resolves()
    {
        var eventId = $"evt-{Guid.NewGuid():N}";
        var ids = (await _seed.TicketsAsync(eventId, "A1")).Select(t => t.Id).ToArray();
        await _seed.ReservationAsync(TestUsers.Owner, eventId, ids);

        using var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Identity-UserId", TestUsers.Owner.ToString());

        using var created = await client.PostAsJsonAsync("api/bookings", new { eventId, tickets = ids });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CreatedBody>())!.Id;
        Assert.NotNull(created.Headers.Location);
        Assert.EndsWith($"/api/bookings/{id}", created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        using var fetched = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    private sealed record CreatedBody(long Id);
}
