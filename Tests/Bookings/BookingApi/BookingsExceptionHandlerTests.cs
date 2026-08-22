using Bookings.Api.Handlers;
using Bookings.Application.Exceptions;
using Bookings.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingApi;

/// <summary>
/// The API contract depends on this mapping: handlers signal failure by throwing, so a wrong arm here
/// turns correct handler logic into the wrong status code. Until this existed, every refused booking
/// answered 500.
/// </summary>
public class BookingsExceptionHandlerTests
{
    private static async Task<(bool Handled, int StatusCode)> HandleAsync(Exception exception)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };

        var handler = new BookingsExceptionHandler(
            context.RequestServices.GetRequiredService<IProblemDetailsService>(),
            NullLogger<BookingsExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        return (handled, context.Response.StatusCode);
    }

    [Fact]
    public async Task Something_that_is_not_there_becomes_404()
    {
        var (handled, status) = await HandleAsync(new NotFoundException("Booking", "7"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    /// <summary>
    /// The set-shaped case — "some of these tickets do not exist" — uses the message constructor and
    /// has to map the same way.
    /// </summary>
    [Fact]
    public async Task A_set_shaped_not_found_becomes_404_too()
    {
        var (_, status) = await HandleAsync(new NotFoundException("Some of the tickets do not exist"));

        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task A_seat_the_world_has_taken_becomes_409()
    {
        var (handled, status) = await HandleAsync(
            new BookingsApplicationException("One of the tickets already reserved"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task A_request_the_domain_refuses_becomes_400()
    {
        var (handled, status) = await HandleAsync(new BookingsDomainException("Too many tickets"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    /// <summary>
    /// <see cref="NotFoundException"/> derives from <see cref="BookingsApplicationException"/>, so
    /// this is the arm that would break first if the switch were ever reordered — 404 would quietly
    /// become 409.
    /// </summary>
    [Fact]
    public async Task Not_found_is_matched_ahead_of_its_base_type()
    {
        var (_, derived) = await HandleAsync(new NotFoundException("Booking", "7"));
        var (_, @base) = await HandleAsync(new BookingsApplicationException("conflicting"));

        Assert.Equal(StatusCodes.Status404NotFound, derived);
        Assert.Equal(StatusCodes.Status409Conflict, @base);
    }

    /// <summary>
    /// Anything else is a genuine bug and must stay a 500 rather than being dressed up as a
    /// well-formed rejection.
    /// </summary>
    [Fact]
    public async Task An_unexpected_failure_is_left_alone()
    {
        var (handled, _) = await HandleAsync(new InvalidOperationException("something broke"));

        Assert.False(handled);
    }
}
