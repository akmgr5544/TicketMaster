using Microsoft.AspNetCore.Diagnostics;
using PaymentSystem.Domain.Exceptions;

namespace PaymentSystem.Shared.Endpoints;

// A domain rule the request broke is the caller's mistake, so it is a 400, not a 500.
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not PaymentDomainException)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Title = "BadRequest", Detail = exception.Message, Status = StatusCodes.Status400BadRequest }
        });
    }
}
