using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using TicketMaster.ApiGateway.Dtos;

namespace TicketMaster.ApiGateway.Handlers;

// The price of the lifetime: a token revoked, or a role changed, in Users.Api keeps working here for up to 30
// seconds. Only successful introspections are stored — a refusal or an outage is asked again next request.
internal sealed class IntrospectionCache : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    // Bounds memory against a flood of distinct valid tokens; past it, new entries are simply not kept.
    private const int MaxEntries = 10_000;

    // Its own instance, so the size limit cannot break any other IMemoryCache user that sets no entry sizes.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly TimeProvider _clock;

    public IntrospectionCache(TimeProvider clock) => _clock = clock;

    public bool TryGet(string token, out UserDto user)
    {
        // The entry's own expiry is checked against the injected clock; MemoryCache's eviction only frees memory.
        if (_cache.TryGetValue(KeyFor(token), out Entry? entry) && _clock.GetUtcNow() < entry!.ExpiresAt)
        {
            user = entry.User;
            return true;
        }

        user = null!;
        return false;
    }

    public void Set(string token, UserDto user) =>
        _cache.Set(KeyFor(token), new Entry(user, _clock.GetUtcNow() + Lifetime),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime, Size = 1 });

    public void Dispose() => _cache.Dispose();

    // A hash, so the live credential is not what sits in memory as a key.
    private static string KeyFor(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed record Entry(UserDto User, DateTimeOffset ExpiresAt);
}
