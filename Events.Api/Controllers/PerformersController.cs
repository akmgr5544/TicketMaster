using Events.Application.Commands;
using Events.Application.Dtos;
using Events.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Events.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerformersController : ControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ISender _sender;

    public PerformersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{id}")]
    [ProducesResponseType<PerformerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPerformerAsync(string id, CancellationToken cancellationToken)
    {
        var performer = await _sender.Send(new GetPerformerQuery(id), cancellationToken);
        return Ok(performer);
    }

    /// <summary>
    /// Pass the <c>continuationToken</c> from the previous response to fetch the next page; a null
    /// token in the response means there are no more.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<PerformerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPerformersAsync(
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Clamp(pageSize, 1, MaxPageSize);

        var performers = await _sender.Send(new ListPerformersQuery(page, continuationToken), cancellationToken);
        return Ok(performers);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddPerformerAsync([FromBody] AddPerformerCommand command,
        CancellationToken cancellationToken)
    {
        var id = await _sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetPerformerAsync), new { id }, new { id });
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePerformerAsync(string id,
        [FromBody] UpdatePerformerCommand command,
        CancellationToken cancellationToken)
    {
        // The route is the address of the resource, so it wins over whatever the body claims.
        await _sender.Send(command with { Id = id }, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeletePerformerAsync(string id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeletePerformerCommand(id), cancellationToken);

        return NoContent();
    }
}
