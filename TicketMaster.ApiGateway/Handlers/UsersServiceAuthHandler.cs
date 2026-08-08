using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TicketMaster.ApiGateway.Dtos;

namespace TicketMaster.ApiGateway.Handlers;

public class UsersServiceAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly HttpClient _httpClient;

    public UsersServiceAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHttpClientFactory clientFactory) : base(options, logger, encoder)
    {
        _httpClient = clientFactory.CreateClient("UsersService");
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var token))
            return AuthenticateResult.Fail("Missing Authorization Header");

        // Forwarded as a header, not a query parameter: query strings land in access logs,
        // proxy logs and browser history, and the value here is a live bearer credential.
        using var introspection = new HttpRequestMessage(HttpMethod.Get, "api/users/auth");
        introspection.Headers.TryAddWithoutValidation("Authorization", token.ToString());

        var response = await _httpClient.SendAsync(introspection, Context.RequestAborted);
        if (!response.IsSuccessStatusCode)
            return AuthenticateResult.Fail("Unauthorized");

        var userInfo = await response.Content.ReadFromJsonAsync<UserDto>();

        if (userInfo == null)
            return AuthenticateResult.Fail("Unauthorized");

        Claim[] claims =
        [
            new("UserId", userInfo.Id),
            new("Email", userInfo.Email),
            new("FirstName", userInfo.FirstName),
            new("LastName", userInfo.LastName),
            new("UserName", userInfo.UserName)
        ];

        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}