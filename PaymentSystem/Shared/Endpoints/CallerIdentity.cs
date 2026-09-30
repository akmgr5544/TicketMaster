namespace PaymentSystem.Shared.Endpoints;

public static class CallerIdentity
{
    public const string UserIdHeader = "X-Identity-UserId";

    // The gateway validates the token and sets this header, replacing anything the client sent. It is the
    // only source of the caller's id: never a route value or a body field, which a caller could choose.
    public static bool TryGetUserId(this HttpContext httpContext, out Guid userId) =>
        Guid.TryParse(httpContext.Request.Headers[UserIdHeader].ToString(), out userId);
}
