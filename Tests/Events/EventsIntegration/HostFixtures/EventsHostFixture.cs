using DotNet.Testcontainers.Containers;
using EventsIntegration.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.RabbitMq;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EventsIntegration.HostFixtures;

/// <summary>
/// Boots the real Events host — <c>Program.cs</c> unmodified, <c>ConfigureRabbitMq</c> and the Cosmos
/// outbox included — against a RabbitMQ container and the Cosmos emulator. The Events analogue of
/// BookingsHostFixture: the one place Wolverine actually starts, so the only place the outbox relay can
/// be observed end to end rather than reasoned about. A successful relay implies the <c>wolverine</c>
/// container was provisioned and the envelope survived the serializer. Its own containers and its own
/// collection, separate from the fast EventsFixture.
/// </summary>
public sealed class EventsHostFixture : IAsyncLifetime
{
    private const string Database = "events_host_test";

    private readonly IContainer _cosmos = CosmosEmulator.NewContainer();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-alpine").Build();

    private WebApplicationFactory<Program>? _factory;

    // A second, minimal Wolverine host on the same broker, standing in for a consuming service such as
    // Bookings. Conventional routing binds its queue to the same exchange the Events host publishes to.
    private IHost? _consumer;

    public IServiceProvider Services =>
        (_factory ?? throw new InvalidOperationException("The host was not started.")).Services;

    public MessageSink Sink { get; } = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_cosmos.StartAsync(), _rabbit.StartAsync());

        // Environment variables, not WebApplicationFactory's hooks: Program.cs reads every connection
        // string while composing the builder, before those hooks run. Safe against the fast
        // EventsFixture because that one builds an explicit in-memory configuration and never reads
        // the environment. ConnectionMode=Gateway is the emulator seam (see CosmosOptions).
        Environment.SetEnvironmentVariable("CosmosConfigs__ConnectionString", CosmosEmulator.ConnectionString(_cosmos));
        Environment.SetEnvironmentVariable("CosmosConfigs__Database", Database);
        Environment.SetEnvironmentVariable("CosmosConfigs__ConnectionMode", nameof(ConnectionMode.Gateway));
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", _rabbit.GetConnectionString());

        _factory = new WebApplicationFactory<Program>();

        // Forces the host to build and start (Wolverine, the Cosmos outbox, EnsureContainersAsync). A
        // startup failure surfaces here, named, rather than as every test failing for no stated reason.
        _ = _factory.Services.GetService(typeof(IServiceProvider));

        _consumer = await StartProbeConsumerAsync();
    }

    private async Task<IHost> StartProbeConsumerAsync()
    {
        var host = Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                // Resolves ConnectionStrings:RabbitMQ from the environment, the same value the Events
                // host uses, so both sit on the one broker.
                options.UseRabbitMqUsingNamedConnection("RabbitMQ")
                    .AutoProvision()
                    .UseConventionalRouting();

                // The handler lives in this test assembly, not the host's entry assembly.
                options.Discovery.IncludeAssembly(typeof(EventCreatedProbeHandler).Assembly);

                // The fixture's own sink instance, so the test reads what the handler received.
                options.Services.AddSingleton(Sink);
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    public async Task DisposeAsync()
    {
        if (_consumer is not null)
            await _consumer.StopAsync();

        if (_factory is not null)
            await _factory.DisposeAsync();

        await Task.WhenAll(_cosmos.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());

        // These are process-global; clear them so a later env-reading host cannot inherit this
        // disposed fixture's dead container endpoints.
        Environment.SetEnvironmentVariable("CosmosConfigs__ConnectionString", null);
        Environment.SetEnvironmentVariable("CosmosConfigs__Database", null);
        Environment.SetEnvironmentVariable("CosmosConfigs__ConnectionMode", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", null);
    }
}

[CollectionDefinition(Name)]
public sealed class EventsHostCollection : ICollectionFixture<EventsHostFixture>
{
    public const string Name = "Events host";
}
