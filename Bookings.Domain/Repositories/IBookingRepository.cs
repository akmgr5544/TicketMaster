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

    /// <summary>
    /// One of this user's bookings, untracked, for reading. Scoped by user in the query rather than
    /// checked afterwards, so a booking belonging to somebody else is indistinguishable from one that
    /// does not exist — which is what the caller should be told either way.
    /// </summary>
    ValueTask<Booking?> FindForUserAsync(long bookingId, string userId, CancellationToken cancellationToken);

    /// <summary>
    /// A page of this user's bookings, newest first, untracked.
    /// </summary>
    ValueTask<Booking[]> ListForUserAsync(string userId,
        int skip,
        int take,
        CancellationToken cancellationToken);
}