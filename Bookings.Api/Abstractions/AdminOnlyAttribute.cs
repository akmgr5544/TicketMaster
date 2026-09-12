using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bookings.Api.Abstractions;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AdminOnlyAttribute : Attribute, IActionFilter
{
    private const string RoleHeader = "X-Identity-Role";
    private const string AdminRole = "Admin";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var role = context.HttpContext.Request.Headers[RoleHeader].FirstOrDefault();

        if (!string.Equals(role, AdminRole, StringComparison.Ordinal))
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
