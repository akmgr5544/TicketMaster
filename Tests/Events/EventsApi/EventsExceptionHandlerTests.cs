using Events.Api.Handlers;
using Events.Application.Exceptions;
using Events.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventsApi;

/// <summary>
/// The API contract depends on this mapping: handlers signal failure by throwing, so a wrong arm
/// here turns correct handler logic into the wrong status code.
/// </summary>
public class EventsExceptionHandlerTests
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

        var handler = new EventsExceptionHandler(
            context.RequestServices.GetRequiredService<IProblemDetailsService>(),
            NullLogger<EventsExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        return (handled, context.Response.StatusCode);
    }

    [Fact]
    public async Task Something_that_is_not_there_becomes_404()
    {
        var (handled, status) = await HandleAsync(new NotFoundException("Venue", "abc"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    /// <summary>
    /// <see cref="NotFoundException"/> derives from <see cref="EventsApplicationException"/>, so
    /// this is the arm that would break first if the switch were ever reordered — 404 would quietly
    /// become 409.
    /// </summary>
    [Fact]
    public async Task Not_found_is_matched_ahead_of_its_base_type()
    {
        var (_, derived) = await HandleAsync(new NotFoundException("Venue", "abc"));
        var (_, @base) = await HandleAsync(new EventsApplicationException("conflicting"));

        Assert.Equal(StatusCodes.Status404NotFound, derived);
        Assert.Equal(StatusCodes.Status409Conflict, @base);
    }

    [Fact]
    public async Task A_use_case_that_cannot_proceed_becomes_409()
    {
        var (handled, status) = await HandleAsync(
            new EventsApplicationException("Venue 'abc' cannot be deleted because it has 3 upcoming event(s)"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task A_broken_domain_rule_becomes_400()
    {
        var (handled, status) = await HandleAsync(new EventsDomainException("Latitude must be…"));

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task Anything_unexpected_is_left_unhandled()
    {
        var (handled, _) = await HandleAsync(new InvalidOperationException("something broke"));

        Assert.False(handled);
    }
}
