using Bookings.Application.Commands;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.EventSync;

internal sealed class RescheduleEventTicketsCommandHandler : IRequestHandler<RescheduleEventTicketsCommand>
{
    private readonly ITicketsRepository _tickets;

    public RescheduleEventTicketsCommandHandler(ITicketsRepository tickets)
    {
        _tickets = tickets;
    }

    public async Task Handle(RescheduleEventTicketsCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetTicketsByEventAsync(request.EventId, cancellationToken);

        foreach (var ticket in tickets)
            ticket.Reschedule(request.StartDate, request.Version);

        await _tickets.SaveChangesAsync(cancellationToken);
    }
}
