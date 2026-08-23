using Medallion.Threading;

namespace Bookings.Application.Extensions;

internal static class TicketLockExtensions
{
    internal static async Task<IAsyncDisposable?> TryAcquireTicketLocksAsync(
        this IDistributedLockProvider lockProvider,
        long[] ticketIds,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var handles = new List<IDistributedSynchronizationHandle>(ticketIds.Length);

        // Ascending id order, never the caller's: it is what stops two overlapping reservations
        // each holding a lock the other is waiting for.
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

internal static class ReservationKeys
{
    internal static string Reservation(long ticketId) => $"bookings:reservation:{ticketId}";
    
    internal static string Lock(long ticketId) => $"bookings:reserve:ticket:{ticketId}";
}