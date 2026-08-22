using Medallion.Threading;

namespace BookingApplication.Fakes;

/// <summary>
/// Records the order locks were taken in and which were released, because with a lock per ticket both
/// are correctness properties rather than details: acquiring in a consistent order is the only thing
/// preventing two overlapping reservations from deadlocking, and a lock left behind on a failed
/// attempt strands a seat until its expiry.
/// </summary>
internal sealed class FakeLockProvider : IDistributedLockProvider
{
    private readonly HashSet<string> _heldElsewhere = [];

    public List<string> Acquired { get; } = [];

    public List<string> Released { get; } = [];

    /// <summary>Simulates another request already holding this lock.</summary>
    public void HoldElsewhere(string name) => _heldElsewhere.Add(name);

    public IDistributedLock CreateLock(string name) => new FakeLock(this, name);

    private sealed class FakeLock : IDistributedLock
    {
        private readonly FakeLockProvider _provider;

        public FakeLock(FakeLockProvider provider, string name)
        {
            _provider = provider;
            Name = name;
        }

        public string Name { get; }

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            if (_provider._heldElsewhere.Contains(Name))
                return ValueTask.FromResult<IDistributedSynchronizationHandle?>(null);

            _provider.Acquired.Add(Name);
            return ValueTask.FromResult<IDistributedSynchronizationHandle?>(new FakeHandle(_provider, Name));
        }

        public async ValueTask<IDistributedSynchronizationHandle> AcquireAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            await TryAcquireAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken)
            ?? throw new TimeoutException(Name);

        public IDistributedSynchronizationHandle? TryAcquire(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default) =>
            TryAcquireAsync(timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

        public IDistributedSynchronizationHandle Acquire(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            AcquireAsync(timeout, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    private sealed class FakeHandle : IDistributedSynchronizationHandle
    {
        private readonly FakeLockProvider _provider;
        private readonly string _name;

        public FakeHandle(FakeLockProvider provider, string name)
        {
            _provider = provider;
            _name = name;
        }

        public CancellationToken HandleLostToken => CancellationToken.None;

        public void Dispose() => _provider.Released.Add(_name);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
