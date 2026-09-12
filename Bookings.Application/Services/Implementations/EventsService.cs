using Bookings.Application.Dtos.EventsServiceDtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Services.Interfaces;
using Grpc.Core;
using TicketMaster.Common.Protos.Events.V1;

namespace Bookings.Application.Services.Implementations;

internal sealed class EventsService : IEventsService
{
    // There is no ambient timeout for a gRPC call — unset means wait forever.
    private static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(5);

    private readonly EventsLookup.EventsLookupClient _client;

    public EventsService(EventsLookup.EventsLookupClient client)
    {
        _client = client;
    }

    public async Task<EventDto?> GetEventByIdAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await _client.GetEventAsync(new GetEventRequest { EventId = id },
                deadline: DateTime.UtcNow.Add(CallDeadline),
                cancellationToken: cancellationToken);

            // In proto3 a message field can be absent on the wire, so Venue may be null even on a
            // successful reply. Treat that as no usable answer rather than dereferencing into an NRE.
            if (reply.Venue is null)
                return null;

            return new EventDto(reply.Id, new VenueDto(reply.Venue.Id, reply.Venue.Name, [..reply.Venue.Seats]));
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
        catch (RpcException exception)
        {
            // Every other status is a transport condition, not an answer about the event. Translated
            // here so no caller ever handles a gRPC type.
            throw new EventsUnavailableException(
                $"The events service could not answer for '{id}' ({exception.StatusCode}).");
        }
    }
}
