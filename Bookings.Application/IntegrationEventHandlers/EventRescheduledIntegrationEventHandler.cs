using Bookings.Application.Commands;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class EventRescheduledIntegrationEventHandler
{
    private readonly IMediator _mediator;

    public EventRescheduledIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(EventRescheduledIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new RescheduleEventTicketsCommand(request.EventId, request.Version, request.StartDate),
            cancellationToken);
    }
}
