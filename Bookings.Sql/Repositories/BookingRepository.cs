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

    public async ValueTask AddAsync(Booking booking)
    {
        await _context.Bookings.AddAsync(booking);
    }

    /// <summary>
    /// Untracked: the caller only reads this. Both predicates are in the query, so a booking that
    /// belongs to another user never leaves the database.
    /// </summary>
    public async ValueTask<Booking?> FindForUserAsync(long bookingId,
        string userId,
        CancellationToken cancellationToken)
    {
        return await _context.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(booking => booking.Id == bookingId && booking.UserId == userId,
                cancellationToken);
    }

    /// <summary>
    /// Ordered by key descending because <c>Booking</c> has no timestamp — the closest thing to
    /// "newest first" available.
    /// </summary>
    public async ValueTask<Booking[]> ListForUserAsync(string userId,
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

    /// <summary>
    /// Tracked on purpose — the caller changes the booking's status and saves. <c>BookedTickets</c> is
    /// an owned collection, so it is loaded without an explicit include, and the cancel path needs it
    /// to know which seats to put back.
    /// </summary>
    public async ValueTask<Booking?> GetByIdAsync(long bookingId, CancellationToken cancellationToken)
    {
        return await _context.Bookings
            .FirstOrDefaultAsync(booking => booking.Id == bookingId, cancellationToken);
    }
}