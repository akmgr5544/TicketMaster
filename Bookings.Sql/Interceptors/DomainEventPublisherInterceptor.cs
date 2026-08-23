using Bookings.Domain.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bookings.Sql.Interceptors;

/// <summary>
/// Publishes the domain events sitting on tracked aggregates once their changes have been written.
/// <para>
/// Dispatch happens after the write, so a handler that needs to change something must save that
/// change itself. The surrounding transaction is what keeps its save and the original write atomic.
/// </para>
/// </summary>
public class DomainEventPublisherInterceptor : SaveChangesInterceptor
{
    private readonly IPublisher _publisher;

    public DomainEventPublisherInterceptor(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = new())
    {
        await PublishDomainEventsAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Synchronous saves would skip event dispatch entirely, and losing a domain event silently is
    /// worse than failing loudly. Nothing in the service saves synchronously; if something starts to,
    /// this says so rather than quietly dropping the event.
    /// </summary>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) =>
        throw new NotSupportedException(
            "Bookings dispatches domain events on asynchronous saves only. Use SaveChangesAsync.");

    private async Task PublishDomainEventsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context == null) return;

        var aggregates = context.ChangeTracker.Entries<Entity>()
            .Where(entry => entry.Entity.DomainEvents.Length > 0)
            .Select(entry => entry.Entity)
            .ToArray();

        if (aggregates.Length == 0) return;

        var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToArray();

        // Cleared before publishing, not after. A handler may save work of its own, which re-enters
        // this interceptor while these aggregates are still tracked — if they still held their events
        // the same events would publish again, and the handler that saved would run again with them.
        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        foreach (var domainEvent in domainEvents)
        {
            await _publisher.Publish(domainEvent, cancellationToken);
        }
    }
}
