using MediatR;

namespace Events.Application.Pipelines;

/// <summary>
/// <b>This provides no transaction.</b> It calls the next handler and returns, and is registered
/// only because the pipeline expects it.
/// <para>
/// Left in place deliberately rather than quietly fixed: under Cosmos there is nothing here to
/// implement. Atomicity is limited to a single logical partition, and with <c>/id</c> as the
/// partition key no two documents ever share one — so a handler that writes two documents cannot
/// be made atomic at this layer. A handler must not assume anything it writes can be rolled back.
/// </para>
/// </summary>
public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        return next(cancellationToken);
    }
}
