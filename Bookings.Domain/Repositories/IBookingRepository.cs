using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface IBookingRepository : IAggregateRoot<Booking>
{
    ValueTask AddAsync(Booking booking);
}