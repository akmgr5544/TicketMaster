using Events.Domain.Entities;
using Events.Domain.Repositories;

namespace EventsApplication.Fakes;

/// <summary>
/// A real in-memory implementation rather than a mock, so the tests assert on what actually
/// happened to the data instead of on which methods were called.
/// </summary>
internal sealed class FakeVenueRepository : IVenueRepository
{
    private readonly Dictionary<string, Venue> _venues = [];

    public string? LastContinuationTokenRequested { get; private set; }
    public string? NextContinuationToken { get; set; }

    public void Seed(params Venue[] venues)
    {
        foreach (var venue in venues)
            _venues[venue.Id] = venue;
    }

    public bool Contains(string id) => _venues.ContainsKey(id);

    public Task<Venue?> GetVenueByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_venues.GetValueOrDefault(id));

    public Task<Page<Venue>> ListVenuesAsync(int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        LastContinuationTokenRequested = continuationToken;

        var items = _venues.Values.Take(pageSize).ToList();
        return Task.FromResult(new Page<Venue>(items, NextContinuationToken));
    }

    public Task AddVenueAsync(Venue venue, CancellationToken cancellationToken)
    {
        _venues[venue.Id] = venue;
        return Task.CompletedTask;
    }

    public Task UpdateVenueAsync(Venue venue, CancellationToken cancellationToken)
    {
        _venues[venue.Id] = venue;
        return Task.CompletedTask;
    }

    public Task DeleteVenueAsync(string id, CancellationToken cancellationToken)
    {
        _venues.Remove(id);
        return Task.CompletedTask;
    }
}
