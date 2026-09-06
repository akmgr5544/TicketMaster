using System.Collections.Immutable;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Sql.Repositories;

internal class TicketsRepository : ITicketsRepository
{
    private readonly BookingDomainContext _context;

    public TicketsRepository(BookingDomainContext context)
    {
        _context = context;
    }

    public async ValueTask<Ticket[]> GetTicketsByIdAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken)
    {
        return await _context.Tickets.Where(x => ticketIds.Contains(x.Id)).ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Tracked on purpose — the caller mutates what comes back and saves. No <c>Update</c> call
    /// follows, so only the columns that actually changed are written.
    /// </summary>
    public async ValueTask<Ticket[]> GetTicketsByEventAsync(string eventId, CancellationToken cancellationToken)
    {
        return await _context.Tickets
            .Where(x => x.EventId == eventId)
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Untracked: the caller only reads these to decide whether the seats can be held.
    /// </summary>
    public async ValueTask<Ticket[]> GetTicketsForReservationAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken)
    {
        return await _context.Tickets
            .AsNoTracking()
            .Where(x => ticketIds.Contains(x.Id))
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<long> GetAppliedVersionForEventAsync(string eventId,
        CancellationToken cancellationToken)
    {
        // Nullable so an event with no tickets comes back as SQL NULL rather than throwing on an
        // empty MAX.
        var applied = await _context.Tickets
            .Where(x => x.EventId == eventId)
            .MaxAsync(x => (long?)x.EventVersion, cancellationToken);

        return applied ?? 0;
    }

    public async ValueTask<bool> SeatIsCoveredAsync(string eventId,
        string seat,
        CancellationToken cancellationToken)
    {
        return await _context.Tickets
            .AnyAsync(x => x.EventId == eventId
                           && x.Seat == seat
                           && x.Status != TicketStatus.Cancelled,
                cancellationToken);
    }

    public async ValueTask AddTicketAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        await _context.Tickets.AddAsync(ticket, cancellationToken);
    }

    public async ValueTask AddTicketsAsync(Ticket[] ticket)
    {
        await _context.Tickets.AddRangeAsync(ticket);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}