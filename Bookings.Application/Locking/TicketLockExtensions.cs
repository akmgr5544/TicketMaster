using Medallion.Threading;

namespace Bookings.Application.Locking;

internal static class TicketLockExtensions
{
    /// <summary>
    /// Takes one lock per ticket and hands back a single handle that releases them all.
    /// <para>
    /// <b>The locks are always taken in ascending ticket id order, never the caller's order, and that
    /// is what makes this deadlock-free.</b> Two requests overlapping on seats 7 and 9 both take 7
    /// before 9, so neither can be left holding what the other is waiting for. Ordering by anything
    /// else — including leaving the caller's order alone — reintroduces that deadlock.
    /// </para>
    /// <para>
    /// Returns <c>null</c> if any lock is unavailable, having released the ones already taken. Callers
    /// must not pass duplicate ids: these locks are not reentrant, so a repeated id waits on a lock
    /// the same request is already holding.
    /// </para>
    /// </summary>
    internal static async Task<IAsyncDisposable?> TryAcquireTicketLocksAsync(
        this IDistributedLockProvider lockProvider,
        long[] ticketIds,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var handles = new List<IDistributedSynchronizationHandle>(ticketIds.Length);

        foreach (var ticketId in ticketIds.Order())
        {
            var handle = await lockProvider.TryAcquireLockAsync(ReservationKeys.Lock(ticketId),
                waitTimeout,
                cancellationToken);

            if (handle is null)
            {
                await new TicketLocks(handles).DisposeAsync();
                return null;
            }

            handles.Add(handle);
        }

        return new TicketLocks(handles);
    }

    private sealed class TicketLocks : IAsyncDisposable
    {
        private readonly List<IDistributedSynchronizationHandle> _handles;

        internal TicketLocks(List<IDistributedSynchronizationHandle> handles)
        {
            _handles = handles;
        }

        public async ValueTask DisposeAsync()
        {
            // Released in reverse, mirroring the order they were taken in.
            for (var i = _handles.Count - 1; i >= 0; i--)
            {
                await _handles[i].DisposeAsync();
            }

            _handles.Clear();
        }
    }
}
