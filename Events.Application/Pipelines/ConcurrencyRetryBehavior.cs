using System.Diagnostics;
using Events.Application.Exceptions;
using Events.Domain.Exceptions;
using MediatR;

namespace Events.Application.Pipelines;

internal sealed class ConcurrencyRetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int MaxAttempts = 3;

    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await next(cancellationToken);
            }
            catch (ConcurrencyConflictException conflict) when (attempt == MaxAttempts)
            {
                // The Cosmos SDK type is long gone by here; this converts the last remaining internal
                // signal into one of the three exceptions the API knows how to map.
                throw new EventsApplicationException(
                    $"{typeof(TRequest).Name} could not be applied: the item kept being changed by "
                    + $"another request across {MaxAttempts} attempts",
                    conflict);
            }
            catch (ConcurrencyConflictException)
            {
                // Lost the race. Go round again: the handler's next act is to re-read.
            }
        }

        // The final attempt either returned or threw, which the compiler cannot see from the loop.
        throw new UnreachableException();
    }
}
