using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PaymentSystem.Domain.Abstractions;

namespace PaymentSystem.Data.Interceptors;

// Publishes after the write, so a handler that changes something must save that change itself; the
// surrounding transaction is what keeps its save atomic with the write that raised the event.
internal sealed class DomainEventPublisherInterceptor(IPublisher publisher) : SaveChangesInterceptor
{
    // Refused before the write rather than after it: throwing from SavedChanges would leave the rows
    // committed and the events lost. A synchronous save with no pending events is harmless and allowed.
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null && PendingAggregates(eventData.Context).Length > 0)
            throw new NotSupportedException(
                "Payments dispatches domain events on asynchronous saves only. Use SaveChangesAsync.");

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            await PublishAsync(eventData.Context, cancellationToken);

        return result;
    }

    private async Task PublishAsync(DbContext context, CancellationToken cancellationToken)
    {
        var aggregates = PendingAggregates(context);
        if (aggregates.Length == 0)
            return;

        var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToArray();

        // Cleared before publishing, not after: a handler that saves re-enters this interceptor while the
        // aggregates are still tracked, and events still on them would publish again — recursion.
        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();

        foreach (var domainEvent in domainEvents)
            await publisher.Publish(domainEvent, cancellationToken);
    }

    private static Entity[] PendingAggregates(DbContext context) =>
        context.ChangeTracker.Entries<Entity>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Length > 0)
            .ToArray();
}
