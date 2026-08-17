using Events.Application.Commands;
using Events.Application.Dtos;
using Events.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Events.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VenuesController : ControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ISender _sender;

    public VenuesController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet("{id}")]
    [ProducesResponseType<VenueDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VenueDto>> GetVenueAsync(string id, CancellationToken cancellationToken)
    {
        var venue = await _sender.Send(new GetVenueQuery(id), cancellationToken);
        return Ok(venue);
    }

    /// <summary>
    /// Pass the <c>continuationToken</c> from the previous response to fetch the next page; a null
    /// token in the response means there are no more.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<VenueDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<VenueDto>>> ListVenuesAsync(
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Clamp(pageSize, 1, MaxPageSize);

        var venues = await _sender.Send(new ListVenuesQuery(page, continuationToken), cancellationToken);
        return Ok(venues);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> AddVenueAsync([FromBody] AddVenueCommand command,
        CancellationToken cancellationToken)
    {
        var id = await _sender.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetVenueAsync), new { id }, new { id });
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateVenueAsync(string id,
        [FromBody] UpdateVenueCommand command,
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
    public async Task<ActionResult> DeleteVenueAsync(string id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteVenueCommand(id), cancellationToken);

        return NoContent();
    }
}
