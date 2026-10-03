using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TicketMaster.ApiGateway.Dtos;

namespace TicketMaster.ApiGateway.Handlers;

internal sealed class UsersServiceAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly HttpClient _httpClient;
    private readonly IntrospectionCache _cache;

    public UsersServiceAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHttpClientFactory clientFactory,
        IntrospectionCache cache) : base(options, logger, encoder)
    {
        _httpClient = clientFactory.CreateClient("UsersService");
        _cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var token))
            return AuthenticateResult.Fail("Missing Authorization Header");

        // StringValues.ToString() joins multiple values with a comma, which would build a malformed
        // credential. A request carrying more than one Authorization header is ambiguous — reject it.
        if (token.Count > 1)
            return AuthenticateResult.Fail("Multiple Authorization Headers");

        var credential = token.ToString();
        if (!_cache.TryGet(credential, out var userInfo))
        {
            var introspected = await IntrospectAsync(credential);
            if (introspected.Failure is not null)
                return introspected.Failure;

            userInfo = introspected.User!;
            _cache.Set(credential, userInfo);
        }

        Claim[] claims =
        [
            new("UserId", userInfo.Id),
            new("Email", userInfo.Email),
            new("FirstName", userInfo.FirstName),
            new("LastName", userInfo.LastName),
            new("UserName", userInfo.UserName),
            new("Role", userInfo.Role)
        ];

        // The authentication type must be set, or ClaimsIdentity.IsAuthenticated stays false and every
        // GatewayAuthPolicy (RequireAuthenticatedUser) check answers 403 despite a valid token.
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private async Task<(UserDto? User, AuthenticateResult? Failure)> IntrospectAsync(string credential)
    {
        // Forwarded as a header, not a query parameter: query strings land in access logs,
        // proxy logs and browser history, and the value here is a live bearer credential.
        using var introspection = new HttpRequestMessage(HttpMethod.Get, "api/users/auth");
        introspection.Headers.TryAddWithoutValidation("Authorization", credential);

        UserDto? userInfo;
        try
        {
            using var response = await _httpClient.SendAsync(introspection, Context.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return (null, AuthenticateResult.Fail("Unauthorized"));

            userInfo = await response.Content.ReadFromJsonAsync<UserDto>(Context.RequestAborted);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Users.Api unreachable or timed out. Surfacing the transport exception would turn every
            // protected route into a 500; a failed introspection is simply "not authenticated".
            Logger.LogWarning(exception, "Token introspection call to the users service failed.");
            return (null, AuthenticateResult.Fail("The authentication service is unavailable"));
        }

        return userInfo is null ? (null, AuthenticateResult.Fail("Unauthorized")) : (userInfo, null);
    }
}