using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface IBookingRepository : IUnitOfWork
{
    ValueTask AddAsync(Booking booking);
}