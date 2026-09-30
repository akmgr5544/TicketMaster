using MediatR;
using PaymentSystem.Data;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Shared.Pipelines;

// Domain events are dispatched after the write, inside SaveChangesAsync; the transaction opened here is what
// makes a handler's own save (wallet credit, ledger pair) atomic with the write that raised the event.
internal sealed class TransactionBehavior<TRequest, TResponse>(PaymentDbContext context)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, ITransactionalRequest
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // A Wolverine message handler already opened a transaction on this scoped context; committing it
        // early would break the outbox guarantee, and a second one on the same connection throws.
        if (context.Database.CurrentTransaction is not null)
            return await next(cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var response = await next(cancellationToken);

        // An expected failure is not an exception, so it would otherwise commit whatever was saved before
        // the handler decided to fail.
        if (response is Result { IsSuccess: false })
        {
            await transaction.RollbackAsync(cancellationToken);
            return response;
        }

        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
