using System.Net;
using System.Net.Http.Headers;
using GatewayTests.Fixtures;

namespace GatewayTests;

// Payments is gated like Bookings and Events, with one carve-out: PSP webhooks carry no user token, so
// "payments-webhooks-route" is ungated and ordered ahead of the catch-all "payments-route". Losing that
// precedence would 401 every webhook; widening it would open the rest of the service.
public sealed class PaymentsRouteTests
{
    [Fact]
    public async Task A_webhook_is_reachable_without_a_token_and_forwarded_without_the_prefix()
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();

        var response = await client.PostAsync("/payments-service/api/payments/webhooks/stripe", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(app.Forwarder.LastForwarded);
        Assert.Equal("/api/payments/webhooks/stripe", app.Forwarder.LastForwarded!.RequestUri!.AbsolutePath);
        // Never asked Users.Api: the route has no policy, so nothing tried to authenticate the caller.
        Assert.Null(app.LastIntrospection);
    }

    [Fact]
    public async Task A_webhook_keeps_the_rest_of_its_path_and_query()
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();

        await client.PostAsync("/payments-service/api/payments/webhooks/braintree?attempt=2", new StringContent("{}"));

        Assert.Equal("/api/payments/webhooks/braintree", app.Forwarder.LastForwarded!.RequestUri!.AbsolutePath);
        Assert.Equal("?attempt=2", app.Forwarder.LastForwarded.RequestUri.Query);
    }

    [Fact]
    public async Task A_webhook_cannot_smuggle_a_caller_identity()
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/payments-service/api/payments/webhooks/stripe")
        {
            Content = new StringContent("{}")
        };
        request.Headers.Add("X-Identity-UserId", "spoofed");

        await client.SendAsync(request);

        Assert.Empty(app.Forwarder.ForwardedValues("X-Identity-UserId"));
    }

    [Theory]
    [InlineData("/payments-service/api/payments/orders/0197a3c4-0000-7000-8000-000000000001/checkout")]
    [InlineData("/payments-service/api/payments/checkouts/42")]
    // Only the webhooks path is carved out, not anything that merely starts the same way.
    [InlineData("/payments-service/api/payments/webhooksx/stripe")]
    [InlineData("/payments-service/api/payments/webhook/stripe")]
    public async Task Every_other_payments_path_without_a_token_is_401_and_never_forwarded(string path)
    {
        using var app = new GatewayApp();
        var client = app.CreateClient();

        var response = await client.PostAsync(path, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(app.Forwarder.LastForwarded);
    }

    [Fact]
    public async Task Another_payments_path_with_a_valid_token_is_forwarded_with_the_callers_identity()
    {
        using var app = new GatewayApp();
        app.IntrospectionSucceeds("buyer-1", "alice", "Customer");
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "good");

        var response = await client.GetAsync("/payments-service/api/payments/checkouts/42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/payments/checkouts/42", app.Forwarder.LastForwarded!.RequestUri!.AbsolutePath);
        Assert.Equal(["buyer-1"], app.Forwarder.ForwardedValues("X-Identity-UserId"));
    }
}
