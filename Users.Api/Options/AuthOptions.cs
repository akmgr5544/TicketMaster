namespace Users.Api.Options;

public record AuthOptions(string Issuer, string Audience, string Token, string RefreshToken);