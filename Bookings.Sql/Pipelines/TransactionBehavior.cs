using Bookings.Domain.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bookings.Sql.Pipelines;

/// <summary>
/// Runs a request that writes inside a database transaction, and runs its after-commit work once that
/// transaction has committed.
/// <para>
/// The <see cref="ITransactionalRequest"/> constraint is what keeps this off requests that never
/// touch the database — the registration is open-generic, so without it every command opened a
/// transaction, including ones that only talk to Redis.
/// </para>
/// </summary>
internal class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, ITransactionalRequest
{
    private readonly BookingDomainContext _context;
    private readonly IAfterCommitQueue _afterCommit;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(BookingDomainContext context,
        IAfterCommitQueue afterCommit,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _context = context;
        _afterCommit = afterCommit;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Wolverine's EF Core middleware already opened a transaction on this context before invoking
        // the message handler that sent this command — that is what puts the outgoing message and the
        // write in one transaction. A second one on the same connection is not possible, and
        // committing Wolverine's early would break the guarantee it exists for. So it commits, not us.
        if (_context.Database.CurrentTransaction is not null)
            return await DeferToTheOwnerAsync(next, cancellationToken);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await next(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await RunAfterCommitWorkAsync(cancellationToken);
            return response;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception,
                "Rolled back the transaction for {Request}", typeof(TRequest).Name);
            throw;
        }
    }

    /// <summary>
    /// After-commit work cannot run here: the commit is somebody else's, so this has no way to know
    /// when it happened. Nothing sends a command that queues any from a message handler today, and if
    /// something starts to, the warning is what says the work was dropped — the same way a failure on
    /// the owned path is reported rather than made fatal.
    /// </summary>
    private async Task<TResponse> DeferToTheOwnerAsync(RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (_afterCommit.HasWork)
            _logger.LogWarning(
                "{Request} queued after-commit work inside a transaction it does not own, so that "
                + "work will not run", typeof(TRequest).Name);

        return response;
    }

    /// <summary>
    /// Failures here are logged rather than thrown. The commit has already happened, so the request
    /// succeeded; reporting it as failed would invite a retry of work that is already done. After-commit
    /// work is cleanup that something else — a TTL, a later sweep — is expected to cover eventually.
    /// </summary>
    private async Task RunAfterCommitWorkAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _afterCommit.RunAllAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "{Request} committed, but its after-commit work failed", typeof(TRequest).Name);
        }
    }
}
