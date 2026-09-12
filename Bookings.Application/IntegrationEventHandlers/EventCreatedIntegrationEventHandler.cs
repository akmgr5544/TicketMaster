using Bookings.Application.Commands;
using MediatR;
using TicketMaster.Common.IntegrationEvents;
using Bookings.Application.Commands.Tickets;

namespace Bookings.Application.IntegrationEventHandlers;

public class EventCreatedIntegrationEventHandler
{
    private readonly ISender _mediator;

    public EventCreatedIntegrationEventHandler(ISender mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(EventCreatedIntegrationEvent request, CancellationToken cancellationToken)
    {
        var command = new CreateTicketsBulkCommand(request.EventId,
            request.VenueId,
            request.EventDate,
            request.Seats,
            request.Version);
        await _mediator.Send(command, cancellationToken);
    }
}