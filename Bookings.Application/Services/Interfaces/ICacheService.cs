namespace Bookings.Application.Services.Interfaces;

public interface ICacheService
{
    Task<List<T>> GetByKeysAsync<T>(string[] keys);
    Task SetToCacheAsync<T>(KeyValuePair<string, T>[] data, TimeSpan? expiration = null);
}