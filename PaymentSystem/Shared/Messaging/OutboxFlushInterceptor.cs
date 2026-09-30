using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Wolverine.EntityFrameworkCore;
using PaymentSystem.Data;

namespace PaymentSystem.Shared.Messaging;

// Flushing is keyed on the transaction that staged the messages, not left to whoever commits: the
// transaction may belong to TransactionBehavior, which knows nothing of messaging. Wolverine's own
// IDbContextOutbox cannot be used for this — its SaveChangesAndFlushMessagesAsync commits the transaction
// itself, which from inside a settlement handler would commit the caller's unit of work early.
//
// One outbox per transaction, never one per scope: a Wolverine MessageContext flushes only once, and one
// reused after a rollback would send the messages whose rows were rolled back.
internal sealed class OutboxFlushInterceptor : DbTransactionInterceptor
{
    private readonly Dictionary<Guid, DbContextOutbox<PaymentDbContext>> _pending = [];

    public DbContextOutbox<PaymentDbContext> OutboxFor(IDbContextTransaction transaction,
        Func<DbContextOutbox<PaymentDbContext>> create)
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
