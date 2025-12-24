using Bookings.Sql;
using MediatR;

namespace Bookings.Application.Pipelines;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly BookingDomainContext _context;

    public TransactionBehavior(BookingDomainContext context)
    {
        _context = context;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        TResponse response;

        await using (var transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                response = await next(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception e)
            {
                await transaction.RollbackAsync(cancellationToken);
                Console.WriteLine(e);
                throw;
            }
        }

        return response;
    }
}