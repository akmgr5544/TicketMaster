using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Data;
using PaymentSystem.Domain.Abstractions;
using PaymentSystem.Domain.Events;
using PaymentSystem.Enums;

namespace PaymentIntegration.Fixtures;

public sealed record PublishedEvent(DomainEvent Event, PaymentDbContext HandlerContext, PaymentOrderStatus? StatusInDatabase);

// Test-only observer for the domain event path. PaymentSystem has no handlers yet, and MediatR only scans
// PaymentSystem's assembly, so the fixture registers RecordingHandler explicitly.
public sealed class DomainEventRecorder
{
    private readonly Lock _gate = new();
    private readonly List<PublishedEvent> _published = [];

    // Runs inside the handler, with the handler's own scope, after the observation is recorded.
    public Func<IServiceProvider, DomainEvent, Task>? OnPublish { get; set; }

    public IReadOnlyList<PublishedEvent> Published
    {
        get { lock (_gate) return [.. _published]; }
    }

    internal void Add(PublishedEvent published)
    {
        lock (_gate) _published.Add(published);
    }

    public void Reset()
    {
        lock (_gate) _published.Clear();
        OnPublish = null;
    }
}

internal sealed class RecordingHandler(
    DomainEventRecorder recorder, PaymentDbContext context, IServiceProvider services, IServiceScopeFactory scopes)
    : INotificationHandler<PaymentOrderSucceededDomainEvent>, INotificationHandler<PaymentOrderFailedDomainEvent>
{
    public Task Handle(PaymentOrderSucceededDomainEvent notification, CancellationToken cancellationToken) =>
        Record(notification, notification.PaymentOrderId, cancellationToken);

    public Task Handle(PaymentOrderFailedDomainEvent notification, CancellationToken cancellationToken) =>
        Record(notification, notification.PaymentOrderId, cancellationToken);

    private async Task Record(DomainEvent domainEvent, Guid paymentOrderId, CancellationToken cancellationToken)
    {
        // A fresh scope is a different connection: it sees only what has been committed.
        PaymentOrderStatus? status;
        await using (var scope = scopes.CreateAsyncScope())
        {
            status = await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Set<PaymentSystem.Domain.PaymentOrder>()
                .Where(o => o.PaymentOrderId == paymentOrderId)
                .Select(o => (PaymentOrderStatus?)o.Status)
                .SingleOrDefaultAsync(cancellationToken);
        }

        recorder.Add(new PublishedEvent(domainEvent, context, status));

        if (recorder.OnPublish is { } onPublish)
            await onPublish(services, domainEvent);
    }
}
