using Bookings.Application.Commands;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class EventRepricedIntegrationEventHandler
{
    private readonly ISender _mediator;

    public EventRepricedIntegrationEventHandler(ISender mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(EventRepricedIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new RepriceEventTicketsCommand(request.EventId, request.Version, request.Pricing),
            cancellationToken);
    }
}
