namespace Bookings.Domain.Abstractions;

/// <summary>
/// Work that must happen only once the database transaction has actually committed — in practice,
/// changes to somewhere that does not roll back with it, such as Redis.
/// <para>
/// A handler runs inside its transaction, so anything it does to an external store there survives a
/// rollback. Queuing that work instead hands it to whatever owns the commit.
/// </para>
/// <para>
/// It lives in the domain project for the same reason <see cref="ITransactionalRequest"/> does: the
/// handlers that queue work are in the application layer, the thing that runs it is infrastructure,
/// and the application layer must not reference infrastructure.
/// </para>
/// </summary>
public interface IAfterCommitQueue
{
    void Enqueue(Func<CancellationToken, Task> work);

    bool HasWork { get; }

    /// <summary>
    /// Runs everything queued, in the order it was queued, and empties the queue.
    /// </summary>
    Task RunAllAsync(CancellationToken cancellationToken);
}
