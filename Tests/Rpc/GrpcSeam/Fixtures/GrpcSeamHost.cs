extern alias EventsApi;

using Bookings.Application.Services.Implementations;
using Events.Application.Queries;
using Events.Domain.Repositories;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TicketMaster.Common.Protos.Events.V1;

namespace GrpcSeam.Fixtures;

// An in-process gRPC round-trip with no real socket: the real EventsLookupService +
// DomainExceptionInterceptor on one side, the real Bookings EventsService on the other, connected by
// a TestServer handler. No Cosmos, no broker, no Docker — a fake repository drives the server's
// exceptions. EventsLookupService and DomainExceptionInterceptor come from Events.Api under the
// EventsApi:: alias; the message types and the client come from Bookings.Application globally.
internal sealed class GrpcSeamHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly GrpcChannel _channel;

    private GrpcSeamHost(IHost host,
        GrpcChannel channel,
        FakeEventRepository repository,
        EventsService client,
        EventsLookup.EventsLookupClient rawClient)
    {
        _host = host;
        _channel = channel;
        Repository = repository;
        Client = client;
        RawClient = rawClient;
    }

    // Configured per test to make the server return, miss, or throw.
    public FakeEventRepository Repository { get; }

    // The real Bookings client under test.
    public EventsService Client { get; }

    // The generated client, for asserting the raw gRPC status the interceptor produces — the two arms
    // (FailedPrecondition, InvalidArgument) that EventsService flattens into EventsUnavailableException
    // and so cannot be told apart at its own boundary.
    public EventsLookup.EventsLookupClient RawClient { get; }

    public static async Task<GrpcSeamHost> StartAsync()
    {
        var repository = new FakeEventRepository();

        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // The real production gRPC wiring — the same AddEventsRpc Program.cs calls, so
                    // the interceptor under test cannot drift from what the host actually runs.
                    EventsApi::Events.Api.Rpc.RpcServiceCollectionExtension.AddEventsRpc(services);
                    services.AddMediatR(cfg =>
                        cfg.RegisterServicesFromAssembly(typeof(GetEventQuery).Assembly));
                    services.AddSingleton<IEventRepository>(repository);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                        endpoints.MapGrpcService<EventsApi::Events.Api.Rpc.EventsLookupService>());
                });
            })
            .StartAsync();

        var testServer = host.GetTestServer();
        var channel = GrpcChannel.ForAddress(testServer.BaseAddress,
            new GrpcChannelOptions { HttpHandler = testServer.CreateHandler() });

        var rawClient = new EventsLookup.EventsLookupClient(channel);
        var client = new EventsService(rawClient);

        return new GrpcSeamHost(host, channel, repository, client, rawClient);
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }
}
