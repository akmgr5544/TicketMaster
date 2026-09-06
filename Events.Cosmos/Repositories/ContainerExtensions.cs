using System.Net;
using Events.Domain.Exceptions;
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
    /// <para>
    /// The ETag comes back alongside the item because a conditional write later needs the version
    /// <i>this</i> read saw; it costs nothing extra, it is already on the response.
    /// </para>
    /// </summary>
    public static async Task<(T? Item, string? ETag)> PointReadWithETagAsync<T>(this Container container,
        string id,
        CancellationToken cancellationToken) where T : class
    {
        try
        {
            var response = await container.ReadItemAsync<T>(id,
                new PartitionKey(id),
                cancellationToken: cancellationToken);

            return (response.Resource, response.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Creates the item and returns the ETag of the document as stored, so a later write in the same
    /// scope is guarded even though nothing read it.
    /// </summary>
    public static async Task<string> CreateAsync<T>(this Container container,
        T item,
        string id,
        CancellationToken cancellationToken)
    {
        var response = await container.CreateItemAsync(item,
            new PartitionKey(id),
            cancellationToken: cancellationToken);

        return response.ETag;
    }

    /// <summary>
    /// Replaces the item only if it still carries <paramref name="etag"/>, and returns the ETag of
    /// the new version. Cosmos answers a mismatch with 412, which is translated here: the SDK's
    /// exception type must not reach application code, on the same terms as NotFound above.
    /// </summary>
    public static async Task<string> ReplaceWithETagAsync<T>(this Container container,
        T item,
        string id,
        string? etag,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReplaceItemAsync(item,
                id,
                new PartitionKey(id),
                IfMatch(etag),
                cancellationToken);

            return response.ETag;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ConcurrencyConflictException(typeof(T).Name, id, exception);
        }
    }

    /// <summary>
    /// Deletes the item only if it still carries <paramref name="etag"/>. Guarded for the same reason
    /// a replace is: the delete guards in this service read the aggregate first and decide from what
    /// they read, so a delete is a read-modify-write like any other.
    /// </summary>
    public static async Task DeleteWithETagAsync<T>(this Container container,
        string id,
        string? etag,
        CancellationToken cancellationToken)
    {
        try
        {
            await container.DeleteItemAsync<T>(id,
                new PartitionKey(id),
                IfMatch(etag),
                cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ConcurrencyConflictException(typeof(T).Name, id, exception);
        }
    }

    // Null options rather than options with a null IfMatchEtag: both are unconditional, and passing
    // the request options only when there is something to say keeps the two cases visibly different.
    private static ItemRequestOptions? IfMatch(string? etag) =>
        etag is null ? null : new ItemRequestOptions { IfMatchEtag = etag };
}
