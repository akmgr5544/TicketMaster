using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.EventSync;

internal sealed class ReconcileEventVenueCommandHandler : IRequestHandler<ReconcileEventVenueCommand>
{
    private readonly ITicketsRepository _tickets;

    public ReconcileEventVenueCommandHandler(ITicketsRepository tickets)
    {
        _tickets = tickets;
    }

    /// <summary>
    /// Reconciles what is held against the seat set the event now has: seats that survive move to the
    /// new venue, seats that no longer exist are cancelled, and seats that are new get a ticket.
    /// Because the command describes the destination rather than the change, running it again is a
    /// no-op.
    /// </summary>
    public async Task Handle(ReconcileEventVenueCommand request, CancellationToken cancellationToken)
    {
        var tickets = await _tickets.GetTicketsByEventAsync(request.EventId, cancellationToken);

        // Rejected as a whole rather than per ticket. Every other handler can lean on Ticket's own
        // staleness guard, but this one *creates* tickets, and a seat that does not exist yet has no
        // version to compare against — a stale message would re-add seats a newer one removed.
        // -1 so that a first message for an event with no tickets is never treated as stale.
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

        // A cancelled ticket does not count as covering its seat. If a seat leaves the event and later
        // comes back, the holder of the cancelled ticket has already been told it is void, so the seat
        // needs a fresh ticket rather than the old one quietly coming back to life.
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
