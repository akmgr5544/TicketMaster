using Bookings.Domain.Abstractions;

namespace BookingApplication.Fakes;

internal sealed class FakeAfterCommitQueue : IAfterCommitQueue
{
    private readonly List<Func<CancellationToken, Task>> _work = [];

    public bool HasWork => _work.Count > 0;

    public void Enqueue(Func<CancellationToken, Task> work) => _work.Add(work);

    /// <summary>Stands in for the transaction committing.</summary>
    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        foreach (var work in _work)
        {
            await work(cancellationToken);
        }

        _work.Clear();
    }
}
