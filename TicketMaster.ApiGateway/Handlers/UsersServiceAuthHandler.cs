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

        var response = await _httpClient.GetAsync($"api/users/auth?token={token}");
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