using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface IBookingRepository : IUnitOfWork
{
    void Add(Booking booking);
    ValueTask<Booking?> GetByIdAsync(long bookingId, CancellationToken cancellationToken);
    ValueTask<Booking?> FindForUserAsync(long bookingId, string userId, CancellationToken cancellationToken);

    ValueTask<Booking[]> ListForUserAsync(string userId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    ValueTask<int> CountForUserAsync(string userId, CancellationToken cancellationToken);
}