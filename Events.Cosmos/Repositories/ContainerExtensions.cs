using System.Net;
using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Repositories;

internal static class ContainerExtensions
{
    /// <summary>
    /// A point read — id plus partition key — which is the cheapest read Cosmos offers. A query
    /// that merely filters on the same two values is not a point read and does not get the price.
    /// <para>
    /// A missing item is an ordinary outcome here, not an exceptional one, so the SDK's NotFound
    /// exception is turned back into null and never escapes the persistence layer.
    /// </para>
    /// </summary>
    public static async Task<T?> PointReadAsync<T>(this Container container,
        string id,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await container.ReadItemAsync<T>(id, new PartitionKey(id), cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
