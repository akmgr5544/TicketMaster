using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface IBookingRepository : IUnitOfWork
{
    ValueTask AddAsync(Booking booking);

    /// <summary>
    /// One booking with the tickets it covers, tracked, because callers settle its payment and save.
    /// Null when no such booking exists.
    /// </summary>
    ValueTask<Booking?> GetByIdAsync(long bookingId, CancellationToken cancellationToken);
}