using Bookings.Domain.Abstractions;

namespace Bookings.Sql;

internal sealed class AfterCommitQueue : IAfterCommitQueue
{
    private readonly Queue<Func<CancellationToken, Task>> _work = new();

    public bool HasWork => _work.Count > 0;

    public void Enqueue(Func<CancellationToken, Task> work)
    {
        _work.Enqueue(work);
    }

    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        while (_work.TryDequeue(out var work))
        {
            await work(cancellationToken);
        }
    }
}
