using Bookings.Application.Exceptions;
using Events.Application.Exceptions;
using Events.Domain.Entities;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;
using Grpc.Core;
using GrpcSeam.Fixtures;
using Proto = TicketMaster.Common.Protos.Events.V1;

namespace GrpcSeam;

// The one seam that is hand-written on both ends: a domain outcome in Events survives the
// exception → gRPC status → translation round trip and reaches the Bookings caller as the caller's
// own contract, rather than degrading to "everything is a transport failure / 503".
public sealed class GrpcErrorRoundTripTests
{
    [Fact]
    public async Task A_not_found_event_comes_back_as_null_not_a_transport_failure()
    {
        await using var host = await GrpcSeamHost.StartAsync();
        host.Repository.NotFound();

        var result = await host.Client.GetEventByIdAsync("missing", CancellationToken.None);

        // Null is the "no such event" contract (IEventsService). If the server interceptor is not
        // translating NotFoundException, this arrives as StatusCode.Unknown and EventsService turns it
        // into EventsUnavailableException instead — the silent 503 degradation this test guards.
        Assert.Null(result);
    }

    [Fact]
    public async Task A_found_event_round_trips_its_venue_and_seats()
    {
        await using var host = await GrpcSeamHost.StartAsync();
        var venue = new Venue("The Forum", "1 Main St", new GeoLocation(40, -70), ["A1", "A2"]);
        var @event = new Event(DateTime.UtcNow.AddDays(14), venue, [new Performer("Headliner", "headliner")]);
        host.Repository.Returns(@event);

        var result = await host.Client.GetEventByIdAsync(@event.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(@event.Id, result!.Id);
        Assert.Equal("The Forum", result.Venue.Name);
        Assert.Equal(["A1", "A2"], result.Venue.Seats);
    }

    [Fact]
    public async Task An_unmapped_server_error_reaches_the_caller_as_unavailable()
    {
        await using var host = await GrpcSeamHost.StartAsync();
        // Deliberately NOT one of the three mapped domain exceptions: an unexpected fault the
        // interceptor does not translate becomes StatusCode.Unknown on the wire. This is the exact
        // "degrades quietly to everything is Internal" case the seam guards — the caller must still
        // flatten it to EventsUnavailableException rather than leak a gRPC type.
        host.Repository.Throws(new InvalidOperationException("unexpected"));

        await Assert.ThrowsAsync<EventsUnavailableException>(
            () => host.Client.GetEventByIdAsync("x", CancellationToken.None));
    }

    // The two interceptor arms EventsService cannot tell apart at its own boundary, observed on the raw
    // status. NotFound is covered above by its behavioural consequence (null); these two have none
    // distinct at the caller, so the status itself is the assertion.
    [Fact]
    public async Task An_application_rule_violation_maps_to_failed_precondition()
    {
        await using var host = await GrpcSeamHost.StartAsync();
        host.Repository.Throws(new EventsApplicationException("nope"));

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => host.RawClient.GetEventAsync(new Proto.GetEventRequest { EventId = "x" }).ResponseAsync);

        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
    }

    [Fact]
    public async Task A_domain_rule_violation_maps_to_invalid_argument()
    {
        await using var host = await GrpcSeamHost.StartAsync();
        host.Repository.Throws(new EventsDomainException("bad"));

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => host.RawClient.GetEventAsync(new Proto.GetEventRequest { EventId = "x" }).ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }
}
