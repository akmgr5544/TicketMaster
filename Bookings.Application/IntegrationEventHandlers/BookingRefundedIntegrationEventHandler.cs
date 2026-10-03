using Bookings.Application.Commands.Payments;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class BookingRefundedIntegrationEventHandler
{
    private readonly ISender _mediator;

    public BookingRefundedIntegrationEventHandler(ISender mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(BookingRefundedIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CompleteRefundCommand(request.BookingId, request.RefundId), cancellationToken);
    }
}
