using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Bookings.Sql.Repositories;

internal class BookingRepository : IBookingRepository
{
    private readonly BookingDomainContext _context;

    public BookingRepository(BookingDomainContext context)
    {
        _context = context;
    }
    
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    // Synchronous Add: AddAsync only exists to await a value generator such as HiLo, and none is
    // configured, so it would add nothing but a state-machine allocation.
    public void Add(Booking booking)
    {
        _context.Bookings.Add(booking);
    }
    
    public async ValueTask<Booking?> FindForUserAsync(long bookingId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _context.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(booking => booking.Id == bookingId && booking.UserId == userId,
                cancellationToken);
    }

    public async ValueTask<Booking[]> ListForUserAsync(Guid userId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return await _context.Bookings
            .AsNoTracking()
            .Where(booking => booking.UserId == userId)
            .OrderByDescending(booking => booking.Id)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
    }

    public async ValueTask<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.Bookings
            .AsNoTracking()
            .CountAsync(booking => booking.UserId == userId, cancellationToken);
    }

    public async ValueTask<Booking?> FindByTicketIdAsync(long ticketId, CancellationToken cancellationToken)
    {
        return await _context.Bookings.FirstOrDefaultAsync(booking => 
                booking.BookedTickets.Any(bt => bt.TicketId == ticketId),
                cancellationToken);
    }


    public async ValueTask<Booking?> GetByIdAsync(long bookingId, CancellationToken cancellationToken)
    {
        return await _context.Bookings
            .FirstOrDefaultAsync(booking => booking.Id == bookingId, cancellationToken);
    }
}