using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Users.Api.Database;
using Users.Api.Entities;

namespace UsersIntegration.Fixtures;

// Every test starts from an empty Users table: first-user-becomes-Admin is meaningless otherwise.
[Collection(UsersHostCollection.Name)]
public abstract class UsersTest : IAsyncLifetime
{
    protected const string Password = "correct-horse-battery";

    protected UsersTest(UsersHostFixture fixture)
    {
        Fixture = fixture;
        Client = fixture.CreateClient();
    }

    protected UsersHostFixture Fixture { get; }

    protected HttpClient Client { get; }

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected Task<HttpResponseMessage> PostRegistrationAsync(string userName) =>
        Client.PostAsJsonAsync("api/users/registration", new
        {
            userName,
            email = $"{userName}@example.com",
            password = Password,
            confirmPassword = Password,
            firstName = "First",
            lastName = "Last",
            phoneNumber = "+10000000000"
        });

    protected async Task<Tokens> RegisterAsync(string userName)
    {
        using var response = await PostRegistrationAsync(userName);
        return await TokensFromAsync(response);
    }

    protected async Task<Tokens> LoginAsync(string userName)
    {
        using var response = await Client.PostAsJsonAsync("api/users/login", new { userName, password = Password });
        return await TokensFromAsync(response);
    }

    private static async Task<Tokens> TokensFromAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            Assert.Fail($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    protected async Task<HttpResponseMessage> PutRoleAsync(string? bearer, string target, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"api/users/{target}/role")
        {
            Content = JsonContent.Create(body)
        };
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        return await Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> PutRoleAsync(string? bearer, Guid target, string role) =>
        PutRoleAsync(bearer, target.ToString(), new { role });

    // Read through a fresh scope so the assertion sees what the database holds, not a tracked instance.
    protected async Task<User> StoredUserAsync(string userName)
    {
        await using var scope = Fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<UsersDomainContext>();
        return await context.Users.AsNoTracking().SingleAsync(u => u.UserName == userName);
    }

    // JwtSecurityTokenHandler writes ClaimTypes.Role out as the short "role".
    protected static string RoleClaimOf(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Single(c => c.Type is "role" or ClaimTypes.Role).Value;

    protected sealed record Tokens(string Token, string RefreshToken);
}
