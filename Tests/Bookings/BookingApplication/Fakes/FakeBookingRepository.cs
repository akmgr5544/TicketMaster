using System.Reflection;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;

namespace BookingApplication.Fakes;

internal sealed class FakeBookingRepository : IBookingRepository
{
    private static readonly PropertyInfo IdProperty = typeof(Booking).GetProperty(nameof(Booking.Id))!;

    private readonly List<Booking> _bookings = [];

    private long _nextId;

    public int SaveCount { get; private set; }

    public IReadOnlyList<Booking> Bookings => _bookings;

    /// <summary>
    /// Assigns the key on the way in, because that is what the database does and the tests need
    /// distinct ids to look bookings up and to page. <c>Booking.Id</c> is init-only by design, so this
    /// is the single place that reaches past it — a test doing so itself would be a smell.
    /// </summary>
    public ValueTask AddAsync(Booking booking)
    {
        IdProperty.SetValue(booking, ++_nextId);
        _bookings.Add(booking);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Booking?> GetByIdAsync(long bookingId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_bookings.SingleOrDefault(booking => booking.Id == bookingId));

    public ValueTask<Booking?> FindForUserAsync(long bookingId,
        string userId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(_bookings.SingleOrDefault(booking =>
            booking.Id == bookingId && booking.UserId == userId));

    /// <summary>
    /// Newest first by key, mirroring the real query — <c>Booking</c> has no timestamp to order by.
    /// </summary>
    public ValueTask<Booking[]> ListForUserAsync(string userId,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(_bookings
            .Where(booking => booking.UserId == userId)
            .OrderByDescending(booking => booking.Id)
            .Skip(skip)
            .Take(take)
            .ToArray());

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
