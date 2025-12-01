using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface IBookingRepository
{
    ValueTask AddAsync(Booking booking);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}