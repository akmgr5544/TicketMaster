using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Users.Api.Entities;
using Users.Api.Options;
using UsersIntegration.Fixtures;

namespace UsersIntegration.Features.SetRole;

public sealed class SetRoleTests : UsersTest
{
    public SetRoleTests(UsersHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task An_admin_can_promote_another_user_and_the_role_persists()
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carol = await StoredUserAsync("carol");

        using var response = await PutRoleAsync(admin.Token, carol.Id, "Admin");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(UserRole.Admin, (await StoredUserAsync("carol")).Role);
    }

    [Fact]
    public async Task A_promoted_user_gets_the_Admin_claim_on_their_next_login()
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carolId = (await StoredUserAsync("carol")).Id;
        using (var promoted = await PutRoleAsync(admin.Token, carolId, "Admin"))
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);

        var carol = await LoginAsync("carol");

        Assert.Equal(nameof(UserRole.Admin), RoleClaimOf(carol.Token));
        using var asAdmin = await PutRoleAsync(carol.Token, (await StoredUserAsync("admin")).Id, "Customer");
        Assert.Equal(HttpStatusCode.NoContent, asAdmin.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_demote_an_admin_back_to_Customer()
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carolId = (await StoredUserAsync("carol")).Id;
        using (var promoted = await PutRoleAsync(admin.Token, carolId, "Admin"))
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);

        using var demoted = await PutRoleAsync(admin.Token, carolId, "Customer");

        Assert.Equal(HttpStatusCode.NoContent, demoted.StatusCode);
        Assert.Equal(UserRole.Customer, (await StoredUserAsync("carol")).Role);
    }

    [Fact]
    public async Task The_role_name_is_matched_case_insensitively()
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carolId = (await StoredUserAsync("carol")).Id;

        using var response = await PutRoleAsync(admin.Token, carolId, "aDmIn");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(UserRole.Admin, (await StoredUserAsync("carol")).Role);
    }

    [Fact]
    public async Task A_customer_is_forbidden_and_the_target_is_unchanged()
    {
        await RegisterAsync("admin");
        var customer = await RegisterAsync("carol");
        var carolId = (await StoredUserAsync("carol")).Id;

        using var response = await PutRoleAsync(customer.Token, carolId, "Admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(UserRole.Customer, (await StoredUserAsync("carol")).Role);
    }

    [Fact]
    public async Task A_request_without_a_token_is_unauthorized()
    {
        await RegisterAsync("admin");
        var adminId = (await StoredUserAsync("admin")).Id;

        using var response = await PutRoleAsync(bearer: null, adminId, "Customer");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(UserRole.Admin, (await StoredUserAsync("admin")).Role);
    }

    // The role is read off the token, so the signature is all that stands between a caller and a self-issued
    // Admin claim. Everything else about this token — issuer, audience, lifetime, subject — is valid.
    [Fact]
    public async Task An_Admin_claim_signed_with_another_key_is_unauthorized()
    {
        await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carol = await StoredUserAsync("carol");
        var forged = ForgeAdminToken(carol);

        using var response = await PutRoleAsync(forged, carol.Id, "Admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(UserRole.Customer, (await StoredUserAsync("carol")).Role);
    }

    [Fact]
    public async Task An_unknown_user_id_is_not_found()
    {
        var admin = await RegisterAsync("admin");

        using var response = await PutRoleAsync(admin.Token, Guid.CreateVersion7(), "Admin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("user_not_found", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_role_that_is_not_a_UserRole_is_a_bad_request_and_changes_nothing()
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carolId = (await StoredUserAsync("carol")).Id;

        using var response = await PutRoleAsync(admin.Token, carolId, "Superuser");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_role", await ProblemCodeAsync(response));
        Assert.Equal(UserRole.Customer, (await StoredUserAsync("carol")).Role);
    }

    [Fact]
    public async Task A_missing_role_is_a_bad_request()
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carolId = (await StoredUserAsync("carol")).Id;

        using var response = await PutRoleAsync(admin.Token, carolId.ToString(), new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_role", await ProblemCodeAsync(response));
    }

    // Enum.TryParse accepts any integer string and a comma-joined list of names, so "7" would be persisted as a
    // role nobody has, and "1" or "Admin,Customer" would promote. Only a role's own name is accepted.
    [Theory]
    [InlineData("7")]
    [InlineData("-1")]
    [InlineData("1")]
    [InlineData("Admin,Customer")]
    public async Task A_role_that_is_not_a_role_name_is_a_bad_request_and_changes_nothing(string role)
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var carolId = (await StoredUserAsync("carol")).Id;

        using var response = await PutRoleAsync(admin.Token, carolId, role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UserRole.Customer, (await StoredUserAsync("carol")).Role);
    }

    [Fact]
    public async Task An_id_that_is_not_a_guid_does_not_match_the_route()
    {
        var admin = await RegisterAsync("admin");

        using var response = await PutRoleAsync(admin.Token, "42", new { role = "Admin" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Characterises current behaviour, not a desired one: AdminOnly reads the role claim baked into the token
    // at issue, never the store, so a demotion takes effect only when the old token expires (one day).
    [Fact]
    public async Task A_demoted_admins_existing_token_still_passes_the_admin_check_until_it_expires()
    {
        var admin = await RegisterAsync("admin");
        await RegisterAsync("carol");
        var adminId = (await StoredUserAsync("admin")).Id;
        var carolId = (await StoredUserAsync("carol")).Id;
        using (var promoted = await PutRoleAsync(admin.Token, carolId, "Admin"))
            Assert.Equal(HttpStatusCode.NoContent, promoted.StatusCode);
        var carol = await LoginAsync("carol");
        using (var demoted = await PutRoleAsync(carol.Token, adminId, "Customer"))
            Assert.Equal(HttpStatusCode.NoContent, demoted.StatusCode);

        using var stale = await PutRoleAsync(admin.Token, carolId, "Customer");

        Assert.Equal(UserRole.Customer, (await StoredUserAsync("admin")).Role);
        Assert.Equal(HttpStatusCode.NoContent, stale.StatusCode);
    }

    private string ForgeAdminToken(User user)
    {
        var options = Fixture.Services.GetRequiredService<IConfiguration>().GetSection("AuthConfigs").Get<AuthOptions>()!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 64)));

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, nameof(UserRole.Admin))
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
