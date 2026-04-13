using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Bookings.Api.Abstractions;

[ApiController]
public class BaseController : ControllerBase
{
    protected long UserId;
    
    
}