using Bookings.Api.Abstractions;
using Bookings.Api.Requests;
using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Bookings.Application.Commands.Bookings;

namespace Bookings.Api.Controllers;

/// <summary>
/// Everything a customer does with their own bookings. Every action is scoped to the caller the
/// gateway resolved, so none of them takes a user id from the request.
/// </summary>
[Route("api/[controller]")]
public class BookingsController : BaseController
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ISender _sender;

    public BookingsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Turns tickets the caller has already reserved into a booking. The reservation must still be
    /// held — this is the step that converts a Redis hold into a durable one.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MakeBookingAsync([FromBody] MakeBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var bookingId = await _sender.Send(
            new MakeBookingCommand(userId, request.EventId, request.Tickets), cancellationToken);

        return CreatedAtAction(nameof(GetBookingAsync), new { id = bookingId }, new { id = bookingId });
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBookingAsync(long id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var booking = await _sender.Send(new GetBookingQuery(id, userId), cancellationToken);
        return Ok(booking);
    }

    /// <summary>
    /// The caller's own bookings, newest first. A full page may mean there are more; <c>Booking</c>
    /// has no timestamp, so there is nothing better than the key to order by.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<BookingDto[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListBookingsAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var bookings = await _sender.Send(
            new ListBookingsQuery(userId, Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize)),
            cancellationToken);

        return Ok(bookings);
    }

    /// <summary>
    /// Cancels a booking the caller has not paid for, putting its seats back on sale. A paid booking
    /// is refused with 400 — undoing that is a refund, which this service does not do.
    /// </summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelBookingAsync(long id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        await _sender.Send(new CancelBookingCommand(id, userId), cancellationToken);
        return NoContent();
    }
}
