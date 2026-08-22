using Bookings.Api.Abstractions;
using Bookings.Api.Requests;
using Bookings.Application.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Bookings.Api.Controllers;

[Route("api/[controller]")]
public class TicketsController : BaseController
{
    private readonly ISender _sender;

    public TicketsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> CreateTicketAsync([FromBody] CreateTicketCommand command,
        CancellationToken token = default)
    {
        await _sender.Send(command, token);
        return Ok();
    }
    
    [HttpPost("reserve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ReserveTicketAsync([FromBody] ReserveTicketsRequest request,
        CancellationToken token = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        await _sender.Send(new ReserveTicketCommand(userId, request.EventId, request.Tickets), token);
        return Ok();
    }
}
