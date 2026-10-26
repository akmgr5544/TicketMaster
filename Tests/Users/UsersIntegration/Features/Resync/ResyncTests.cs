using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using UsersIntegration.Fixtures;

namespace UsersIntegration.Features.Resync;

// Services that subscribed after users already existed get every account announced again on an admin's request.
public sealed class ResyncTests : UsersTest
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    public ResyncTests(UsersHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task An_admin_resync_announces_every_existing_user_again()
    {
        var admin = UniqueUserName();
        var customer = UniqueUserName();
        var adminTokens = await RegisterAsync(admin);
        await RegisterAsync(customer);
        // Both registrations arriving first means a second message per user can only be the resync's.
        await Fixture.Published.WaitForAsync(Email(admin), Wait);
        await Fixture.Published.WaitForAsync(Email(customer), Wait);

        using var response = await PostResyncAsync(adminTokens.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<ResyncResponse>())!.Published);
        await WaitForCountAsync(Email(admin), 2);
        await WaitForCountAsync(Email(customer), 2);
        var customerId = (await StoredUserAsync(customer)).Id;
        Assert.All(Fixture.Published.For(Email(customer)), message => Assert.Equal(customerId, message.UserId));
    }

    [Fact]
    public async Task A_customer_is_forbidden_and_nothing_is_announced()
    {
        var admin = UniqueUserName();
        var customer = UniqueUserName();
        await RegisterAsync(admin);
        var customerTokens = await RegisterAsync(customer);
        await Fixture.Published.WaitForAsync(Email(customer), Wait);

        using var response = await PostResyncAsync(customerTokens.Token);
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(Fixture.Published.For(Email(customer)));
    }

    [Fact]
    public async Task A_request_without_a_token_is_unauthorized()
    {
        using var response = await PostResyncAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostResyncAsync(string? bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/users/resync");
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        return await Client.SendAsync(request);
    }

    private async Task WaitForCountAsync(string email, int count)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (Fixture.Published.For(email).Count >= count)
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Expected {count} UserRegistered for {email}; got {Fixture.Published.For(email).Count}.");
    }

    private static string UniqueUserName() => $"user{Guid.NewGuid():N}";

    private static string Email(string userName) => $"{userName}@example.com";

    private sealed record ResyncResponse(int Published);
}
