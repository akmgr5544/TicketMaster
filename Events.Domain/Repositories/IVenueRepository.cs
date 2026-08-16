using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IVenueRepository
{
    Task<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken);
    Task AddVenueAsync(Venue venue, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the aggregate rather than loose fields so an update cannot bypass the venue's own
    /// validation — callers load it, call Rename/Relocate, and save it back.
    /// </summary>
    Task UpdateVenueAsync(Venue venue, CancellationToken cancellationToken);

    Task DeleteVenueAsync(string id, CancellationToken cancellationToken);
}
