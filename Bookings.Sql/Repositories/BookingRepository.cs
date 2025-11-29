using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;

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
}