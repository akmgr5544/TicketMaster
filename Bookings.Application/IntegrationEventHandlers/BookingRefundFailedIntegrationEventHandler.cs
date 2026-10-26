using Bookings.Application.Commands.Payments;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

public class BookingRefundFailedIntegrationEventHandler
{
    private readonly ISender _mediator;

    public BookingRefundFailedIntegrationEventHandler(ISender mediator)
    {
        _mediator = mediator;
    }

    public async Task Consume(BookingRefundFailedIntegrationEvent request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new FailRefundCommand(request.BookingId, request.RefundId), cancellationToken);
    }
}
