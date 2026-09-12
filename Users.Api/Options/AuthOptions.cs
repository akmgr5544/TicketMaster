namespace Users.Api.Options;

// RefreshTokenDays must exceed the access token's lifetime (1 day) or refresh is pointless — the
// refresh token would die at the same moment as the token it exists to replace. Defaulted so no
// appsettings change is required.
public record AuthOptions(string Issuer, string Audience, string Token, int RefreshTokenDays = 7);