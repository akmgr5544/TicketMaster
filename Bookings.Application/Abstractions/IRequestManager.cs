namespace Bookings.Application.Abstractions;

public interface IRequestManager
{
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);
    Task CreateRequestAsync(Guid id, CancellationToken cancellationToken);
}