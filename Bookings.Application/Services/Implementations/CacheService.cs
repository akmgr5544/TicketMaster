using Bookings.Application.Services.Interfaces;
using StackExchange.Redis;
using Newtonsoft.Json;

namespace Bookings.Application.Services.Implementations;

internal class CacheService : ICacheService
{
    private readonly IDatabase _cacheDb;

    public CacheService(IDatabase cacheDb)
    {
        _cacheDb = cacheDb;
    }

    public async Task<List<T>> GetByKeysAsync<T>(string[] keys)
    {
        var redisKeys = keys.Distinct().Select(key => new RedisKey(key)).ToArray();
        var values = await _cacheDb.StringGetAsync(redisKeys);

        var listResult = new List<T>();
        foreach (var redisValue in values)
        {
            if (!redisValue.HasValue)
                continue;

            var result = JsonConvert.DeserializeObject<T>(redisValue.ToString());
            listResult.Add(result!);
        }

        return listResult;
    }

    public async Task SetToCacheAsync<T>(KeyValuePair<string, T>[] data, TimeSpan? expiration = null)
    {
        var batch = _cacheDb.CreateBatch();
        if (!expiration.HasValue)
            expiration = TimeSpan.FromMinutes(2);

        var tasks = new List<Task>();

        foreach (var item in data)
        {
            tasks.Add(batch.StringSetAsync(item.Key, JsonConvert.SerializeObject(item.Value), expiration));
        }
        batch.Execute();
        await Task.WhenAll(tasks);
    }
}