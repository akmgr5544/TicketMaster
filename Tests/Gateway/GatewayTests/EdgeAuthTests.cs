using System.Net;
using System.Net.Http.Headers;
using GatewayTests.Fixtures;

namespace GatewayTests;

// The edge-auth behaviour of UsersServiceAuthHandler: a protected route is only reachable when
// introspection succeeds, and a failed introspection is "not authenticated" (401), never a 500.
public sealed class EdgeAuthTests
{
    [Fact]
    public async Task A_protected_route_without_a_token_is_401()
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();

        var response = await client.GetAsync("/bookings-service/api/bookings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_protected_route_with_a_rejected_token_is_401()
    {
        using var app = new GatewayApp();
        app.IntrospectionReturns(HttpStatusCode.Unauthorized);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "rejected");

        var response = await client.GetAsync("/bookings-service/api/bookings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_protected_route_is_401_not_500_when_introspection_is_unreachable()
    {
        using var app = new GatewayApp();
        app.IntrospectionIsUnreachable();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "anything");

        var response = await client.GetAsync("/bookings-service/api/bookings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_users_route_is_reachable_without_a_token()
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();

        // Ungated: no policy, so it proxies without authentication. The forwarder stub answers 200.
        var response = await client.GetAsync("/users-service/api/users/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_bearer_token_is_forwarded_to_the_introspection_endpoint_as_a_header()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("u", "n", "Customer");
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "the-token");

        await client.GetAsync("/bookings-service/api/bookings");

        Assert.NotNull(app.LastIntrospection);
        // A header, not a query string — a live credential must not land in logs. And the well-known
        // introspection path, not somewhere else.
        Assert.Equal("/api/users/auth", app.LastIntrospection!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer the-token", app.LastIntrospection.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task More_than_one_Authorization_header_is_rejected()
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/bookings-service/api/bookings");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer one");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer two");

        var response = await client.SendAsync(request);

        // Ambiguous credentials — the handler refuses before ever calling introspection.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
