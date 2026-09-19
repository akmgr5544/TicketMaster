using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Forwarder;

namespace GatewayTests.Fixtures;

// Boots the real gateway in-process. Two seams are stubbed and nothing else: the "UsersService"
// HttpClient (the introspection call) and YARP's outbound forwarder (so a proxied request is captured
// instead of dialling a real downstream). No Docker, no ports.
internal sealed class GatewayApp : WebApplicationFactory<Program>
{
    public GatewayApp()
    {
        // Program.cs reads this while composing the builder — before any ConfigureAppConfiguration hook
        // could supply it — so it has to be on the environment before the host is built. Never dialled:
        // the UsersService handler is stubbed below.
        Environment.SetEnvironmentVariable("Services__Users__BaseAddress", "http://users.invalid");
    }

    // Set per test to shape the introspection response. Defaults to the "unreachable" signal
    // (HttpRequestException) the handler catches, so a test that forgets to arrange gets a clean 401
    // rather than a 500 that reads as a gateway bug.
    public Func<HttpResponseMessage> OnIntrospect { get; set; } =
        () => throw new HttpRequestException("Introspection behaviour was not configured.");

    public CapturingForwarder Forwarder { get; } = new();

    // The last request the handler sent to Users.Api, so a test can assert how the token was forwarded.
    public HttpRequestMessage? LastIntrospection { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Replace the introspection client's transport. ConfigurePrimaryHttpMessageHandler here runs
            // after Program.cs registered the client, so it wins.
            services.AddHttpClient("UsersService")
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(this));

            // Replace YARP's real outbound HTTP client with one that records the forwarded request and
            // answers 200, so identity-propagation can be observed without a downstream.
            services.RemoveAll<IForwarderHttpClientFactory>();
            services.AddSingleton<IForwarderHttpClientFactory>(Forwarder);
        });
    }

    public void IntrospectionSucceeds(string id, string userName, string role) =>
        OnIntrospect = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                id,
                email = "user@example.com",
                firstName = "Given",
                lastName = "Family",
                userName,
                role,
                permissions = Array.Empty<string>()
            })
        };

    public void IntrospectionReturns(HttpStatusCode status) =>
        OnIntrospect = () => new HttpResponseMessage(status);

    public void IntrospectionIsUnreachable() =>
        OnIntrospect = () => throw new HttpRequestException("users service down");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly GatewayApp _owner;

        public StubHandler(GatewayApp owner) => _owner = owner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Record before responding so a test can assert the handler forwarded the token to the
            // right endpoint, as a header rather than a query parameter.
            _owner.LastIntrospection = request;
            return Task.FromResult(_owner.OnIntrospect());
        }
    }
}

// Captures the last request YARP tried to forward, so a test can read the X-Identity-* headers the
// transform put on it, and answers 200 so the proxy path completes.
internal sealed class CapturingForwarder : IForwarderHttpClientFactory
{
    public HttpRequestMessage? LastForwarded { get; private set; }

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
        new(new CapturingHandler(this));

    public IReadOnlyList<string> ForwardedValues(string header) =>
        LastForwarded is not null && LastForwarded.Headers.TryGetValues(header, out var values)
            ? values.ToArray()
            : [];

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly CapturingForwarder _owner;

        public CapturingHandler(CapturingForwarder owner) => _owner = owner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _owner.LastForwarded = request;

            // Match the request version so YARP does not trip on a protocol mismatch.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = request.Version,
                Content = new ByteArrayContent([])
            });
        }
    }
}
