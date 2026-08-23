using Bookings.Application.Commands;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.EventSync;

internal sealed class ReconcileEventVenueCommandHandler : IRequestHandler<ReconcileEventVenueCommand>
{
    private readonly ITicketsRepository _tickets;

    public ReconcileEventVenueCommandHandler(ITicketsRepository tickets)
    {
        _tickets = tickets;
    }

    public async Task Handle(ReconcileEventVenueCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetTicketsByEventAsync(request.EventId, cancellationToken);
        
        var applied = tickets.Select(ticket => ticket.EventVersion).DefaultIfEmpty(-1).Max();
        if (request.Version <= applied)
            return;

        var wanted = request.Seats.ToHashSet();

        foreach (var ticket in tickets)
        {
            if (wanted.Contains(ticket.Seat))
                ticket.Relocate(request.VenueId, request.Version);
            else
                ticket.Cancel(request.Version);
        }

        var covered = tickets
            .Where(ticket => ticket.Status != TicketStatus.Cancelled)
            .Select(ticket => ticket.Seat)
            .ToHashSet();

        var missing = wanted.Except(covered)
            .Select(seat => new Ticket(seat, request.VenueId, request.EventId, request.StartDate, request.Version))
            .ToArray();

        if (missing.Length > 0)
            await _tickets.AddTicketsAsync(missing);

        await _tickets.SaveChangesAsync(cancellationToken);
    }
}
