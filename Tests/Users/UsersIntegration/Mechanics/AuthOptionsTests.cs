using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Users.Api.Options;
using UsersIntegration.Fixtures;

namespace UsersIntegration.Mechanics;

// Program.cs binds AuthOptions twice: section.Get<AuthOptions>() for JWT validation, which can construct a
// positional record, and Configure<AuthOptions>(section) for IOptions<AuthOptions>, which cannot — the options
// factory needs a parameterless constructor. Startup succeeds either way; only the handlers that take
// IOptions<AuthOptions> (register, login, refresh) find out, as a 500.
[Collection(UsersHostCollection.Name)]
public sealed class AuthOptionsTests
{
    private readonly UsersHostFixture _fixture;

    public AuthOptionsTests(UsersHostFixture fixture) => _fixture = fixture;

    [Fact]
    public void The_handlers_auth_options_resolve_from_configuration()
    {
        var options = _fixture.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

        Assert.Equal("UserServiceAuth", options.Issuer);
        Assert.False(string.IsNullOrWhiteSpace(options.Token));
    }
}
