using Events.Domain.Entities;
using Events.Domain.Repositories;

namespace EventsApplication.Fakes;

/// <summary>
/// A real in-memory implementation rather than a mock, so the tests assert on what actually
/// happened to the data instead of on which methods were called.
/// </summary>
internal sealed class FakePerformerRepository : IPerformerRepository
{
    private readonly Dictionary<string, Performer> _performers = [];

    public string? LastContinuationTokenRequested { get; private set; }
    public string? NextContinuationToken { get; set; }

    public void Seed(params Performer[] performers)
    {
        foreach (var performer in performers)
            _performers[performer.Id] = performer;
    }

    public bool Contains(string id) => _performers.ContainsKey(id);

    public Task<Performer?> GetPerformerByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_performers.GetValueOrDefault(id));

    public Task<IReadOnlyList<Performer>> GetPerformersByIdsAsync(IEnumerable<string> ids,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Performer> found = [..ids.Distinct()
            .Select(_performers.GetValueOrDefault)
            .OfType<Performer>()];

        return Task.FromResult(found);
    }

    public Task<Page<Performer>> ListPerformersAsync(int pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        LastContinuationTokenRequested = continuationToken;

        var items = _performers.Values.Take(pageSize).ToList();
        return Task.FromResult(new Page<Performer>(items, NextContinuationToken));
    }

    public Task AddPerformerAsync(Performer performer, CancellationToken cancellationToken)
    {
        _performers[performer.Id] = performer;
        return Task.CompletedTask;
    }

    public Task UpdatePerformerAsync(Performer performer, CancellationToken cancellationToken)
    {
        _performers[performer.Id] = performer;
        return Task.CompletedTask;
    }

    public Task DeletePerformerAsync(string id, CancellationToken cancellationToken)
    {
        _performers.Remove(id);
        return Task.CompletedTask;
    }
}
