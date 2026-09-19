using System.Net.Http.Headers;
using GatewayTests.Fixtures;

namespace GatewayTests;

// AuthTransformProvider projects the authenticated caller onto the proxied request as X-Identity-*
// headers, and — the security-critical part — it is the ONLY source of them: a client-supplied one is
// replaced, not appended, so a caller cannot smuggle an identity past the gateway.
public sealed class IdentityPropagationTests
{
    [Fact]
    public async Task Identity_from_introspection_is_projected_onto_the_proxied_request()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("user-123", "alice", "Admin");
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "good");

        await client.GetAsync("/bookings-service/api/bookings");

        Assert.Equal(["user-123"], app.Forwarder.ForwardedValues("X-Identity-UserId"));
        Assert.Equal(["alice"], app.Forwarder.ForwardedValues("X-Identity-UserName"));
        Assert.Equal(["Admin"], app.Forwarder.ForwardedValues("X-Identity-Role"));
    }

    [Fact]
    public async Task A_client_supplied_identity_header_is_replaced_not_appended()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("trusted-1", "bob", "Customer");
        var client = app.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/bookings-service/api/bookings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "good");
        // Spoof all three — each is removed before the trusted value is set, or a downstream could read
        // the spoofed one ahead of it.
        request.Headers.Add("X-Identity-UserId", "spoofed");
        request.Headers.Add("X-Identity-UserName", "spoofed");
        request.Headers.Add("X-Identity-Role", "Admin");

        await client.SendAsync(request);

        Assert.Equal(["trusted-1"], app.Forwarder.ForwardedValues("X-Identity-UserId"));
        Assert.Equal(["bob"], app.Forwarder.ForwardedValues("X-Identity-UserName"));
        Assert.Equal(["Customer"], app.Forwarder.ForwardedValues("X-Identity-Role"));
    }

    [Fact]
    public async Task A_client_cannot_smuggle_identity_through_the_ungated_users_route()
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();

        // No token, and the users route is anonymous — but the transform still runs on it, so a
        // client-supplied identity header must be stripped rather than forwarded intact. The one case
        // the transform's own comment says it exists for.
        var request = new HttpRequestMessage(HttpMethod.Get, "/users-service/api/users/login");
        request.Headers.Add("X-Identity-UserId", "spoofed");

        await client.SendAsync(request);

        Assert.Empty(app.Forwarder.ForwardedValues("X-Identity-UserId"));
    }
}
