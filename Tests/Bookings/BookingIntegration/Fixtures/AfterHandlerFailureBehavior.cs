using Bookings.Domain.Abstractions;
using MediatR;

namespace BookingIntegration.Fixtures;

/// <summary>
/// A test-only switch a test flips on to make <see cref="AfterHandlerFailureBehavior{TRequest,TResponse}"/>
/// throw. Off by default, and scoped, so it resets to off with every test's fresh <c>Act</c> scope and
/// never leaks into a test that never touches it. See <see cref="BookingCreatedPublishCounter"/> for the
/// same per-scope observability pattern, used here to inject rather than observe.
/// </summary>
public sealed class AfterHandlerFailureSwitch
{
    public bool ShouldFailAfterHandler { get; set; }
}

/// <summary>
/// Throws once the inner pipeline — the real handler and everything closer to it — has already
/// returned, but before whatever registered this behavior first (in production, only
/// <c>TransactionBehavior</c>) gets to commit. That is the one place a test can inject a genuine
/// post-enqueue, in-transaction failure: nothing inside <c>MakeBookingCommandHandler</c> itself can run
/// after its own last statement enqueues the reservation cleanup, so the only way to prove that cleanup
/// really waits for a commit that may never come is to fail after the handler is done and before the
/// transaction decides.
/// <para>
/// Registered as an open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/> in
/// <see cref="BookingsFixture"/>, after <c>AddInfrastructureServices</c> — MediatR builds the pipeline in
/// registration order, outermost first, so registering after <c>TransactionBehavior</c> is what nests
/// this one instance inside it. The throw here is therefore caught by <c>TransactionBehavior</c>'s own
/// catch block, which rolls back and never drains <c>IAfterCommitQueue</c> — exactly the branch that
/// mattered to the fake-based original and was otherwise unreachable from inside the handler.
/// </para>
/// <para>
/// Constrained to <see cref="ITransactionalRequest"/>, the same as <c>TransactionBehavior</c> itself:
/// there is no transaction to roll back for a request that never opens one, so wrapping every request
/// would be both meaningless and a change to the DI-registration counts
/// <c>TransactionBehaviorRegistrationTests</c> asserts against non-transactional commands like
/// <c>ReserveTicketCommand</c>.
/// </para>
/// </summary>
internal sealed class AfterHandlerFailureBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, ITransactionalRequest
{
    private readonly AfterHandlerFailureSwitch _switch;

    public AfterHandlerFailureBehavior(AfterHandlerFailureSwitch @switch)
    {
        _switch = @switch;
    }

    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (_switch.ShouldFailAfterHandler)
            throw new InvalidOperationException(
                "Test-induced failure after the handler completed, so the surrounding transaction " +
                "rolls back without ever draining the after-commit queue.");

        return response;
    }
}
