using System.Security.Claims;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace TicketMaster.ApiGateway.Transforms;

public class AuthTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        if (string.IsNullOrEmpty(context.Route.AuthorizationPolicy))
        {
            return;
        }

        context.AddRequestTransform(transformContext =>
        {
            var user = transformContext.HttpContext.User;

            if (user.Identity?.IsAuthenticated == true)
            {
                var userId = user.FindFirstValue("UserId");
                var userName = user.FindFirstValue("UserName");
                
                transformContext.ProxyRequest.Headers.Add("X-Identity-UserId", userId);
                transformContext.ProxyRequest.Headers.Add("X-Identity-UserName", userName);
                
            }

            return ValueTask.CompletedTask;
        });
    }
}