using PaymentSystem.Data;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace PaymentSystem.Shared.Messaging;

// Stages each message as a row in Wolverine's envelope tables, written on the DbContext's own connection and
// transaction — whoever opened it, TransactionBehavior or a caller. OutboxFlushInterceptor sends the staged
// messages once that transaction commits, and drops them if it rolls back. A scheduled message is held in the
// store until it is due, so it survives a restart too.
internal sealed class OutboxIntegrationEventPublisher(
    IWolverineRuntime runtime,
    PaymentDbContext context,
    OutboxFlushInterceptor flusher) : IIntegrationEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class =>
        StageAsync(outbox => outbox.PublishAsync(integrationEvent), cancellationToken);

    public Task ScheduleAsync<TMessage>(TMessage message, TimeSpan delay, CancellationToken cancellationToken)
        where TMessage : class =>
        StageAsync(outbox => outbox.ScheduleAsync(message, delay), cancellationToken);

    private async Task StageAsync(Func<DbContextOutbox<PaymentDbContext>, ValueTask> stage,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is { } open)
        {
            await stage(flusher.OutboxFor(open, NewOutbox));
            return;
        }

        await using var own = await context.Database.BeginTransactionAsync(cancellationToken);
        await stage(flusher.OutboxFor(own, NewOutbox));
        await own.CommitAsync(cancellationToken);
    }

    private DbContextOutbox<PaymentDbContext> NewOutbox() => new(runtime, context, []);
}
