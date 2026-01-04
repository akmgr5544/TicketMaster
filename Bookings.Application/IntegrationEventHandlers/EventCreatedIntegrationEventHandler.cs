using Bookings.Application.Commands;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class EventCreatedIntegrationEventHandler
{
    private readonly IMediator _mediator;

    public EventCreatedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(EventCreatedIntegrationEvent request, CancellationToken cancellationToken)
    {
        var command = new CreateTicketsBulkCommand(request.EventId, request.VenueId, request.EventDate, request.Seats);
        await _mediator.Send(command, cancellationToken);
    }
}