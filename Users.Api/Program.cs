using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Users.Api.Extensions;
using Users.Api.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Without these an unhandled exception is a bare 500 with no body. Users throws nothing of its own —
// it signals failure with Result<T> — so there is no domain mapping to add, only a floor under what
// escapes.
builder.Services.AddProblemDetails();

var configuration = builder.Configuration;
builder.Services.AddDatabase(configuration);
builder.Services.AddBusinessServices(configuration);

var section = configuration.GetSection("AuthConfigs");
builder.Services.Configure<AuthOptions>(section);
var authOptions = section.Get<AuthOptions>()!;

// The signing key is a secret and is intentionally not committed: supply it via user-secrets locally
// or AuthConfigs__Token in the environment. Fail fast rather than boot with an unusable empty key.
if (string.IsNullOrWhiteSpace(authOptions.Token))
    throw new InvalidOperationException(
        "'AuthConfigs:Token' is not configured. Set it via user-secrets or the AuthConfigs__Token environment variable.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters()
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.Token))
        };
    });

builder.Services.AddAuthorization(options =>
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(Users.Api.Entities.UserRole.Admin))));

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.ApplyMigrationsAsync();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapEndpoints();

await app.RunAsync();