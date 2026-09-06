namespace Events.Cosmos.Repositories;

internal sealed class ETagCache
{
    private readonly Dictionary<string, string> _etags = [];

    public void Record(string id, string? etag)
    {
        // A read that missed has no ETag; there is nothing to remember and nothing to guard.
        if (etag is not null)
            _etags[id] = etag;
    }
    
    public string? For(string id) => _etags.GetValueOrDefault(id);
}
