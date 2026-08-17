using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IVenueRepository
{
    Task<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Pass the continuation token from the previous page to fetch the next; null starts at the
    /// beginning. The returned token is null once there are no further pages.
    /// </summary>
    Task<Page<Venue>> ListVenuesAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken);

    Task AddVenueAsync(Venue venue, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the aggregate rather than loose fields so an update cannot bypass the venue's own
    /// validation — callers load it, call Rename/Relocate, and save it back.
    /// </summary>
    Task UpdateVenueAsync(Venue venue, CancellationToken cancellationToken);

    Task DeleteVenueAsync(string id, CancellationToken cancellationToken);
}
