using Bookings.Api.Abstractions;
using Bookings.Application.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Bookings.Api.Controllers;

[Route("api/[controller]")]
public class TicketsController : BaseController
{
    private readonly IMediator _mediator;

    public TicketsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<ActionResult> CreateTicketAsync([FromBody] CreateTicketCommand command, CancellationToken token = default)
    {
        await _mediator.Send(command, token);
        return Ok();
    }

    [HttpPost("reserve")]
    public async Task<ActionResult> ReserveTicketAsync([FromBody] ReserveTicketCommand command,
        CancellationToken token = default)
    {
        await _mediator.Send(command, token);
        return Ok();
    }
    
    
}