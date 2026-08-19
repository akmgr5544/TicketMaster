using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.EventSync;

internal sealed class RescheduleEventTicketsCommandHandler : IRequestHandler<RescheduleEventTicketsCommand>
{
    private readonly ITicketsRepository _tickets;

    public RescheduleEventTicketsCommandHandler(ITicketsRepository tickets)
    {
        _tickets = tickets;
    }

    /// <summary>
    /// The tickets come back tracked, so mutating them and saving is enough — no <c>Update</c> call,
    /// which would rewrite every column instead of just the date and version.
    /// </summary>
    public async Task Handle(RescheduleEventTicketsCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetTicketsByEventAsync(request.EventId, cancellationToken);

        foreach (var ticket in tickets)
            ticket.Reschedule(request.StartDate, request.Version);

        await _tickets.SaveChangesAsync(cancellationToken);
    }
}
