using Bookings.Domain.Abstractions;
using Bookings.Sql.Interceptors;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace Bookings.Sql;

// Stages each message as a row in Wolverine's outgoing envelope table, on the context's own connection
// and transaction — whoever opened it. OutboxFlushInterceptor sends the staged messages once that
// transaction commits, and drops them if it rolls back.
internal sealed class OutboxIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IWolverineRuntime _runtime;
    private readonly BookingDomainContext _context;
    private readonly OutboxFlushInterceptor _flusher;

    public OutboxIntegrationEventPublisher(IWolverineRuntime runtime,
        BookingDomainContext context,
        OutboxFlushInterceptor flusher)
    {
        _runtime = runtime;
        _context = context;
        _flusher = flusher;
    }

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : class
    {
        if (_context.Database.CurrentTransaction is { } open)
        {
            await _flusher.OutboxFor(open, NewOutbox).PublishAsync(integrationEvent);
            return;
        }

        // Every publisher today runs inside a transactional command, so this is a fallback: the message is
        // still persisted before it is sent, but it is no longer atomic with a write already saved.
        await using var own = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _flusher.OutboxFor(own, NewOutbox).PublishAsync(integrationEvent);
        await own.CommitAsync(cancellationToken);
    }

    private DbContextOutbox<BookingDomainContext> NewOutbox() => new(_runtime, _context, []);
}
