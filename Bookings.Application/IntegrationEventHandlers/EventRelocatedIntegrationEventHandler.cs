using Bookings.Application.Commands;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

/// <summary>
/// A relocation changes which seats exist, so this is the one change that can create and cancel
/// tickets rather than just edit them.
/// </summary>
public class EventRelocatedIntegrationEventHandler
{
    private readonly ISender _mediator;

    public EventRelocatedIntegrationEventHandler(ISender mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(EventRelocatedIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new ReconcileEventVenueCommand(request.EventId,
                request.Version,
                request.VenueId,
                request.StartDate,
                request.Seats),
            cancellationToken);
    }
}
