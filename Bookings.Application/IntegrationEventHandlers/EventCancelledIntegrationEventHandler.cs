using Bookings.Application.Commands;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class EventCancelledIntegrationEventHandler
{
    private readonly IMediator _mediator;

    public EventCancelledIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(EventCancelledIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelEventTicketsCommand(request.EventId, request.Version), cancellationToken);
    }
}
