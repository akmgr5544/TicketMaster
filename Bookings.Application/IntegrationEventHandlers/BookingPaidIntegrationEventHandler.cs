using Bookings.Application.Commands;
using Bookings.Application.Commands.Payments;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class BookingPaidIntegrationEventHandler
{
    private readonly IMediator _mediator;

    public BookingPaidIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(BookingPaidIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ConfirmBookingCommand(request.BookingId), cancellationToken);
    }
}
