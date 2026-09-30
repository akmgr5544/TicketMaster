using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Wolverine.EntityFrameworkCore;

namespace Bookings.Sql.Interceptors;

// Flushing is keyed on the transaction that staged the messages, not left to whoever commits: that may be
// TransactionBehavior or Wolverine's own EF middleware, and neither knows these messages exist. Wolverine's
// IDbContextOutbox cannot be used instead — SaveChangesAndFlushMessagesAsync commits the transaction
// itself, which would commit TransactionBehavior's unit of work before the handler had finished.
//
// One outbox per transaction, never one per scope: a Wolverine MessageContext flushes only once, and one
// reused after a rollback would send messages whose rows were rolled back.
internal sealed class OutboxFlushInterceptor : DbTransactionInterceptor
{
    private readonly Dictionary<Guid, DbContextOutbox<BookingDomainContext>> _pending = [];

    public DbContextOutbox<BookingDomainContext> OutboxFor(IDbContextTransaction transaction,
        Func<DbContextOutbox<BookingDomainContext>> create)
    {
        if (!_pending.TryGetValue(transaction.TransactionId, out var outbox))
            _pending[transaction.TransactionId] = outbox = create();
        return outbox;
    }

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (_pending.Remove(eventData.TransactionId, out var outbox))
            await outbox.FlushOutgoingMessagesAsync();
    }

    // Nothing commits synchronously today; if something does, the messages must still go.
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (_pending.Remove(eventData.TransactionId, out var outbox))
            outbox.FlushOutgoingMessagesAsync().GetAwaiter().GetResult();
    }

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pending.Remove(eventData.TransactionId);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        _pending.Remove(eventData.TransactionId);

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        _pending.Remove(eventData.TransactionId);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) =>
        _pending.Remove(eventData.TransactionId);
}
