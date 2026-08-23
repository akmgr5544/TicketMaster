using Bookings.Application.Commands;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.EventSync;

internal sealed class CancelEventTicketsCommandHandler : IRequestHandler<CancelEventTicketsCommand>
{
    private readonly ITicketsRepository _tickets;

    public CancelEventTicketsCommandHandler(ITicketsRepository tickets)
    {
        _tickets = tickets;
    }

    public async Task Handle(CancelEventTicketsCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetTicketsByEventAsync(request.EventId, cancellationToken);

        foreach (var ticket in tickets)
            ticket.Cancel(request.Version);

        await _tickets.SaveChangesAsync(cancellationToken);
    }
}
