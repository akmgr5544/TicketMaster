using Bookings.Application.Payments;
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
        await _mediator.Send(new ConfirmBookingPaymentCommand(request.BookingId), cancellationToken);
    }
}
