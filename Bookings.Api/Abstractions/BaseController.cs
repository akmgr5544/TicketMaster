using Microsoft.AspNetCore.Mvc;

namespace Bookings.Api.Abstractions;

[ApiController]
public class BaseController : ControllerBase
{
    /// <summary>
    /// The gateway authenticates the caller and puts their id here. It is the only identity source
    /// downstream — Bookings does not read <c>Authorization</c> and does not validate tokens.
    /// </summary>
    private const string IdentityHeader = "X-Identity-UserId";

    /// <summary>
    /// The id travels as a string on the wire (JWT subject → gateway header); Bookings parses it into a
    /// <see cref="Guid"/> here so it never leaks past the edge. False — which callers turn into a 401 —
    /// when the header is absent, blank, or not a Guid: a booking attributed to a guessed or malformed
    /// user is worse than a refused request, and the request body must never be able to supply this.
    /// </summary>
    protected bool TryGetUserId(out Guid userId)
    {
        var value = Request.Headers[IdentityHeader].FirstOrDefault();

        return Guid.TryParse(value, out userId);
    }
}
