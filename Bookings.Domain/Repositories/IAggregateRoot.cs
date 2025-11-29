using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface IAggregateRoot<T> where T : RootEntity
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}