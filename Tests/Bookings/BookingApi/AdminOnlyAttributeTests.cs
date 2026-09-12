using Bookings.Api.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace BookingApi;

/// <summary>
/// The admin repair endpoint mints real, bookable seats, so the filter must fail closed: only the
/// trusted <c>X-Identity-Role: Admin</c> header the gateway sets gets through, and anything else —
/// absent, blank, or another role — is refused before the action runs. A caller reaching the service
/// directly, without the gateway, has no such header and so is refused too.
/// </summary>
public class AdminOnlyAttributeTests
{
    private static ActionExecutingContext ContextWithRole(string? role)
    {
        var httpContext = new DefaultHttpContext();
        if (role is not null)
            httpContext.Request.Headers["X-Identity-Role"] = role;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);
    }

    private static int? Refused(string? role)
    {
        var context = ContextWithRole(role);
        new AdminOnlyAttribute().OnActionExecuting(context);
        return (context.Result as StatusCodeResult)?.StatusCode;
    }

    [Fact]
    public void Allows_an_admin_caller()
    {
        var context = ContextWithRole("Admin");

        new AdminOnlyAttribute().OnActionExecuting(context);

        // Not short-circuited: the action is allowed to run.
        Assert.Null(context.Result);
    }

    [Theory]
    [InlineData(null)]       // reached the service directly, without the gateway
    [InlineData("")]         // header present but blank
    [InlineData("Customer")] // an ordinary authenticated caller
    [InlineData("admin")]    // case must match exactly
    public void Refuses_anything_that_is_not_admin(string? role)
    {
        Assert.Equal(StatusCodes.Status403Forbidden, Refused(role));
    }
}
