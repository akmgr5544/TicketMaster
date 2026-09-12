using System.Security.Cryptography;
using System.Text;
using Users.Api.Features.Users;
using Users.Api.Options;

namespace UsersApi;

public class TokenServiceTests
{
    private static AuthOptions Options(int refreshTokenDays = 7) =>
        new("issuer", "audience", new string('a', 64), refreshTokenDays);

    [Fact]
    public void HashRefreshToken_is_sha256_hex_lowercase()
    {
        const string raw = "a-refresh-token";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

        Assert.Equal(expected, TokenService.HashRefreshToken(raw));
    }

    [Fact]
    public void HashRefreshToken_is_deterministic()
    {
        Assert.Equal(TokenService.HashRefreshToken("same"), TokenService.HashRefreshToken("same"));
    }

    [Fact]
    public void HashRefreshToken_produces_64_hex_chars_that_fit_the_column()
    {
        var hash = TokenService.HashRefreshToken("anything");

        Assert.Equal(64, hash.Length);
        Assert.True(hash.Length <= 120);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void CreateRefreshToken_persists_only_the_hash_and_hands_back_the_raw()
    {
        var result = TokenService.CreateRefreshToken(Options());

        // The raw value goes to the client; the stored value is its hash, never the raw token.
        Assert.NotEqual(result.Raw, result.Hash);
        Assert.Equal(TokenService.HashRefreshToken(result.Raw), result.Hash);
    }

    [Fact]
    public void CreateRefreshToken_is_random_per_call()
    {
        Assert.NotEqual(TokenService.CreateRefreshToken(Options()).Raw,
            TokenService.CreateRefreshToken(Options()).Raw);
    }

    [Fact]
    public void CreateRefreshToken_outlives_the_one_day_access_token()
    {
        var result = TokenService.CreateRefreshToken(Options(7));

        Assert.True(result.Expires > DateTime.UtcNow.AddDays(1));
    }
}
