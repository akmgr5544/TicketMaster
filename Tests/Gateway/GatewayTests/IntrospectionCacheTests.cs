using System.Net;
using System.Net.Http.Headers;
using GatewayTests.Fixtures;

namespace GatewayTests;

// A successful introspection is reused for 30 seconds, so a burst of requests costs Users.Api one call. Only
// success is kept: a refusal or an outage must be asked again, or one blip would lock a caller out.
public sealed class IntrospectionCacheTests
{
    private const string Route = "/bookings-service/api/bookings";

    private static HttpClient ClientWith(GatewayApp app, string token)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task A_token_is_introspected_once_within_the_lifetime()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("user-1", "name", "Customer");
        var client = ClientWith(app, "token-a");

        var first = await client.GetAsync(Route);
        app.Clock.Advance(TimeSpan.FromSeconds(29));
        var second = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, app.IntrospectionCount);
    }

    [Fact]
    public async Task A_cached_identity_still_reaches_the_downstream_service()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("user-1", "name", "Admin");
        var client = ClientWith(app, "token-a");
        await client.GetAsync(Route);

        await client.GetAsync(Route);

        Assert.Equal(["user-1"], app.Forwarder.ForwardedValues("X-Identity-UserId"));
        Assert.Equal(["Admin"], app.Forwarder.ForwardedValues("X-Identity-Role"));
    }

    [Fact]
    public async Task A_token_is_introspected_again_once_the_lifetime_has_passed()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("user-1", "name", "Customer");
        var client = ClientWith(app, "token-a");
        await client.GetAsync(Route);

        app.Clock.Advance(TimeSpan.FromSeconds(30));
        await client.GetAsync(Route);

        Assert.Equal(2, app.IntrospectionCount);
    }

    [Fact]
    public async Task Each_token_is_introspected_for_itself()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("user-1", "name", "Customer");

        await ClientWith(app, "token-a").GetAsync(Route);
        await ClientWith(app, "token-b").GetAsync(Route);

        Assert.Equal(2, app.IntrospectionCount);
    }

    [Fact]
    public async Task A_rejected_token_is_not_cached()
    {
        using var app = new GatewayApp();
        app.IntrospectionReturns(HttpStatusCode.Unauthorized);
        var client = ClientWith(app, "token-a");
        var refused = await client.GetAsync(Route);

        app.IntrospectionSucceeds("user-1", "name", "Customer");
        var accepted = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(2, app.IntrospectionCount);
    }

    [Fact]
    public async Task An_unreachable_users_service_is_not_cached()
    {
        using var app = new GatewayApp();
        app.IntrospectionIsUnreachable();
        var client = ClientWith(app, "token-a");
        var refused = await client.GetAsync(Route);

        app.IntrospectionSucceeds("user-1", "name", "Customer");
        var accepted = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }
}
