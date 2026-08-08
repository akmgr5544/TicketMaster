using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace TicketMaster.ApiGateway.Transforms;

/// <summary>
/// Projects the authenticated caller's identity onto the proxied request as X-Identity-* headers.
/// Downstream services trust these headers, so the gateway must be their only source.
/// </summary>
internal class AuthTransformProvider : ITransformProvider
{
    private const string UserIdHeader = "X-Identity-UserId";
    private const string UserNameHeader = "X-Identity-UserName";

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        // Applied to every route, including anonymous ones. YARP copies all incoming request
        // headers to the proxied request by default, so a route that skipped this would forward
        // whatever X-Identity-* headers the caller invented.
        context.AddRequestTransform(transformContext =>
        {
            // Remove before adding: HttpHeaders.Add appends rather than replaces, so a
            // client-supplied value would survive and be read ahead of the trusted one.
            transformContext.ProxyRequest.Headers.Remove(UserIdHeader);
            transformContext.ProxyRequest.Headers.Remove(UserNameHeader);

            var user = transformContext.HttpContext.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return ValueTask.CompletedTask;
            }

            SetIdentityHeader(transformContext, UserIdHeader, user.FindFirstValue("UserId"));
            SetIdentityHeader(transformContext, UserNameHeader, user.FindFirstValue("UserName"));

            return ValueTask.CompletedTask;
        });
    }

    private static void SetIdentityHeader(RequestTransformContext context, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            context.ProxyRequest.Headers.Add(name, value);
        }
    }
}