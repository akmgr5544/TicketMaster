using Events.Application.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Events.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerformersController : ControllerBase
{
    private readonly ISender _sender;

    public PerformersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    public async Task<ActionResult> AddPerformerAsync([FromBody] AddPerformerCommand command,
        CancellationToken cancellationToken)
    {
        await _sender.Send(command, cancellationToken);
        return Ok();
    }
}
