using System.Diagnostics.CodeAnalysis;
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
    /// False when the header is absent, which callers turn into a 401. Deliberately not defaulted to
    /// anything: a booking attributed to an empty or guessed user is worse than a refused request, and
    /// the request body must never be able to supply this.
    /// </summary>
    protected bool TryGetUserId([NotNullWhen(true)] out string? userId)
    {
        var value = Request.Headers[IdentityHeader].FirstOrDefault();

        userId = string.IsNullOrWhiteSpace(value) ? null : value;
        return userId is not null;
    }
}
