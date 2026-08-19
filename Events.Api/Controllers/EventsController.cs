using Events.Application.Commands;
using Events.Application.Dtos;
using Events.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Events.Api.Controllers;

/// <summary>
/// Mutations are separate sub-resources rather than one <c>PUT /api/events/{id}</c>, which is a
/// deliberate departure from the venues and performers controllers. Each change here has a different
/// downstream consequence — moving venues changes which seats exist, rescheduling does not — and a
/// combined PUT would have to work out which happened by diffing.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ISender _sender;

    public EventsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{id}")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEventAsync(string id, CancellationToken cancellationToken)
    {
        var @event = await _sender.Send(new GetEventQuery(id), cancellationToken);
        return Ok(@event);
    }

    /// <summary>
    /// Pass the <c>continuationToken</c> from the previous response to fetch the next page; a null
    /// token in the response means there are no more.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<EventDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEventsAsync(
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Clamp(pageSize, 1, MaxPageSize);

        var events = await _sender.Send(new ListEventsQuery(page, continuationToken), cancellationToken);
        return Ok(events);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateEventAsync([FromBody] CreateEventCommand command,
        CancellationToken cancellationToken)
    {
        var id = await _sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetEventAsync), new { id }, new { id });
    }

    [HttpPut("{id}/schedule")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RescheduleEventAsync(string id,
        [FromBody] RescheduleEventCommand command,
        CancellationToken cancellationToken)
    {
        // The route is the address of the resource, so it wins over whatever the body claims.
        await _sender.Send(command with { Id = id }, cancellationToken);

        return NoContent();
    }

    [HttpPut("{id}/venue")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RelocateEventAsync(string id,
        [FromBody] RelocateEventCommand command,
        CancellationToken cancellationToken)
    {
        await _sender.Send(command with { Id = id }, cancellationToken);

        return NoContent();
    }

    [HttpPut("{id}/lineup")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeEventLineupAsync(string id,
        [FromBody] ChangeEventLineupCommand command,
        CancellationToken cancellationToken)
    {
        await _sender.Send(command with { Id = id }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// A state transition rather than a removal, so it is POST-to-action and not DELETE: the event
    /// document survives, and downstream tickets are cancelled rather than deleted. Idempotent —
    /// cancelling an already-cancelled event succeeds and announces nothing.
    /// </summary>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelEventAsync(string id, CancellationToken cancellationToken)
    {
        await _sender.Send(new CancelEventCommand(id), cancellationToken);

        return NoContent();
    }
}
