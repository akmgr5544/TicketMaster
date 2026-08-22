using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;

namespace BookingApplication.Fakes;

internal sealed class FakeBookingRepository : IBookingRepository
{
    private readonly List<Booking> _bookings = [];

    public int SaveCount { get; private set; }

    public IReadOnlyList<Booking> Bookings => _bookings;

    public ValueTask AddAsync(Booking booking)
    {
        _bookings.Add(booking);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Booking?> GetByIdAsync(long bookingId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_bookings.SingleOrDefault(booking => booking.Id == bookingId));

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
