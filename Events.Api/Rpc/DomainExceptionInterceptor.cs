using Events.Application.Exceptions;
using Events.Domain.Exceptions;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Events.Api.Rpc;

/// <summary>
/// The gRPC counterpart of <see cref="Handlers.EventsExceptionHandler"/>: status codes do not cross
/// this boundary, so the same three exceptions are mapped again here. Arms stay most-derived-first,
/// because <see cref="NotFoundException"/> derives from <see cref="EventsApplicationException"/> and
/// an earlier base arm would swallow it — the compiler does not catch that in a catch chain.
/// </summary>
internal sealed class DomainExceptionInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (NotFoundException exception)
        {
            throw new RpcException(new Status(StatusCode.NotFound, exception.Message));
        }
        catch (EventsApplicationException exception)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
        catch (EventsDomainException exception)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, exception.Message));
        }
    }
}
