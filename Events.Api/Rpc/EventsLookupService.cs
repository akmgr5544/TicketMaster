using Events.Application.Queries;
using Grpc.Core;
using MediatR;
using TicketMaster.Common.Protos.Events.V1;

namespace Events.Api.Rpc;

internal sealed class EventsLookupService : EventsLookup.EventsLookupBase
{
    private readonly ISender _sender;

    public EventsLookupService(ISender sender)
    {
        _sender = sender;
    }

    public override async Task<GetEventReply> GetEvent(GetEventRequest request, ServerCallContext context)
    {
        // context.CancellationToken is what the caller's deadline raises; passing it on is what stops
        // the query running here after the caller has already given up.
        var @event = await _sender.Send(new GetEventQuery(request.EventId), context.CancellationToken);

        return new GetEventReply
        {
            Id = @event.Id,
            Venue = new Venue
            {
                Id = @event.Venue.Id,
                Name = @event.Venue.Name,
                Seats = { @event.Venue.Seats }
            }
        };
    }
}
