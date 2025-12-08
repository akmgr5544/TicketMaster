using System.Collections.Immutable;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Sql.Repositories;

public class TicketsRepository : ITicketsRepository
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