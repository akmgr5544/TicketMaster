using MediatR;
using MongoDB.Driver;

namespace Events.Application.Pipelines;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IMongoClient _mongoClient;

    public TransactionBehavior(IMongoClient mongoClient)
    {
        _mongoClient = mongoClient;
    }
    
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        //TODO:: add MQ transaction as well
        var response =  default(TResponse);
        using (var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken))
        {
            session.StartTransaction();
            try
            {
                response = await next(cancellationToken);
                await session.CommitTransactionAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await session.AbortTransactionAsync(cancellationToken: cancellationToken);
            }
        }
        return response;
    }
}