using MediatR;
using MongoDB.Driver;

namespace Events.Application.Pipelines;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        //TODO:: add MQ transaction as well
        var response = await next(cancellationToken);

        return response;
    }
}