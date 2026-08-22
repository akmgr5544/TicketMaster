using Bookings.Application.Exceptions;
using Bookings.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Bookings.Api.Handlers;

/// <summary>
/// Turns the three exceptions Bookings throws into status codes and a <c>ProblemDetails</c> body.
/// <para>
/// The arms must stay in most-derived-first order, but that is enforced rather than remembered:
/// <see cref="NotFoundException"/> derives from <see cref="BookingsApplicationException"/>, so
/// putting the base first makes the derived arm unreachable and the build fails with CS8510.
/// </para>
/// </summary>
internal sealed class BookingsExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<BookingsExceptionHandler> _logger;

    public BookingsExceptionHandler(IProblemDetailsService problemDetailsService,
        ILogger<BookingsExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = Map(exception);

        // Anything else is genuinely unexpected: leave it unhandled so it surfaces as a 500 rather
        // than being dressed up as a well-formed response.
        if (statusCode is null)
            return false;

        _logger.LogInformation(exception,
            "Request rejected with {StatusCode}: {Message}",
            statusCode,
            exception.Message);

        httpContext.Response.StatusCode = statusCode.Value;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception.Message,
                Type = $"https://httpstatuses.io/{statusCode}"
            }
        });
    }

    private static (int? StatusCode, string? Title) Map(Exception exception) => exception switch
    {
        // Asked for something that isn't there.
        NotFoundException => (StatusCodes.Status404NotFound, "Not found"),

        // The request is well formed and the model intact, but the world says no: the seat has gone,
        // the reservation lapsed, the booking is already settled.
        BookingsApplicationException => (StatusCodes.Status409Conflict, "Conflict"),

        // An entity refused the change, or the request was rejected before reaching one.
        BookingsDomainException => (StatusCodes.Status400BadRequest, "Invalid request"),

        _ => (null, null)
    };
}
