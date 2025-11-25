using System.Drawing;
using Events.Domain.Entities;

namespace Events.Domain.Repositories;

public interface IVenueRepository
{
    ValueTask<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken);
    Task AddVenueAsync(Venue venue, CancellationToken cancellationToken);

    public Task UpdateVenueAsync(string id,
        string name,
        string address,
        Point location,
        CancellationToken cancellationToken);
    Task DeleteVenueAsync(string id, CancellationToken cancellationToken);
}