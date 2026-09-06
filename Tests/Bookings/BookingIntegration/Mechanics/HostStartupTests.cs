using BookingIntegration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace BookingIntegration.Mechanics;

[Collection(BookingsHostCollection.Name)]
public sealed class HostStartupTests
{
    private readonly BookingsHostFixture _fixture;

    public HostStartupTests(BookingsHostFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The whole point of the project. Every Wolverine risk this repo carries — a durability policy
    /// that was never applied, a handler dependency Wolverine cannot resolve, a code-generation mode
    /// with no compiler behind it — is a startup failure that compiles cleanly and leaves every other
    /// suite green.
    /// </summary>
    [Fact]
    public void The_host_starts()
    {
        Assert.NotNull(_fixture.Services.GetRequiredService<IWolverineRuntime>());
    }

    [Fact]
    public void Every_broker_listener_is_durable()
    {
        var listeners = BrokerEndpoints().Where(endpoint => endpoint.IsListener).ToArray();

        Assert.NotEmpty(listeners);
        Assert.All(listeners, endpoint => Assert.Equal(EndpointMode.Durable, endpoint.Mode));
    }

    [Fact]
    public void Every_broker_sender_is_durable()
    {
        var senders = BrokerEndpoints().Where(endpoint => endpoint.Subscriptions.Count > 0).ToArray();

        Assert.All(senders, endpoint => Assert.Equal(EndpointMode.Durable, endpoint.Mode));
    }

    private Endpoint[] BrokerEndpoints()
    {
        var runtime = _fixture.Services.GetRequiredService<IWolverineRuntime>();

        // Wolverine adds its own control and reply endpoints; only the application's own broker
        // endpoints are governed by the durability policies.
        return runtime.Options.Transports.AllEndpoints()
            .Where(endpoint => endpoint.Uri.Scheme == "rabbitmq" && endpoint.Role == EndpointRole.Application)
            .ToArray();
    }
}
