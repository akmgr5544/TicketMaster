using Bookings.Application.Services.Interfaces;

namespace BookingApplication.Fakes;

/// <summary>
/// A real in-memory dictionary rather than a mock, so the tests assert on what is actually cached
/// instead of on which methods were called.
/// </summary>
internal sealed class FakeCacheService : ICacheService
{
    private readonly Dictionary<string, object> _entries = [];

    public List<TimeSpan> Expirations { get; } = [];

    public IReadOnlyCollection<string> Keys => _entries.Keys;

    public void Seed(string key, object value) => _entries[key] = value;

    public Task<List<T>> GetByKeysAsync<T>(string[] keys) =>
        Task.FromResult(keys.Distinct()
            .Where(_entries.ContainsKey)
            .Select(key => (T)_entries[key])
            .ToList());

    public Task SetToCacheAsync<T>(KeyValuePair<string, T>[] data, TimeSpan? expiration = null)
    {
        if (expiration.HasValue)
            Expirations.Add(expiration.Value);

        foreach (var item in data)
        {
            _entries[item.Key] = item.Value!;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string[] keys)
    {
        foreach (var key in keys)
        {
            _entries.Remove(key);
        }

        return Task.CompletedTask;
    }
}
