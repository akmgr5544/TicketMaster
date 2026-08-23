using Bookings.Application.Commands;
using Bookings.Application.Commands.Payments;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class BookingPaymentFailedIntegrationEventHandler
{
    private readonly IMediator _mediator;

    public BookingPaymentFailedIntegrationEventHandler(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(BookingPaymentFailedIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ReleaseUnpaidBookingCommand(request.BookingId), cancellationToken);
    }
}
