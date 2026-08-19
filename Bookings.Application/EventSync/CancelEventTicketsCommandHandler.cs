using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.EventSync;

internal sealed class CancelEventTicketsCommandHandler : IRequestHandler<CancelEventTicketsCommand>
{
    private readonly ITicketsRepository _tickets;

    public CancelEventTicketsCommandHandler(ITicketsRepository tickets)
    {
        _tickets = tickets;
    }

    /// <summary>
    /// Cancels rather than deletes, matching the catalogue: Events keeps the cancelled event document,
    /// and a booking that pointed at these tickets still needs to be explicable afterwards.
    /// </summary>
    public async Task Handle(CancelEventTicketsCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetTicketsByEventAsync(request.EventId, cancellationToken);

        foreach (var ticket in tickets)
            ticket.Cancel(request.Version);

        await _tickets.SaveChangesAsync(cancellationToken);
    }
}
