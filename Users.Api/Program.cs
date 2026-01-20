using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Users.Api.Database;
using Users.Api.Extensions;
using Users.Api.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var configuration = builder.Configuration;
builder.Services.AddDbContext<UsersDomainContext>(options =>
{
    options.UseNpgsql(configuration.GetConnectionString("UsersDatabase"));
    //More configurations
});
//builder.Services.AddDatabase(configuration);
builder.Services.AddBusinessServices(configuration);

var section = configuration.GetSection("AuthConfigs");
builder.Services.Configure<AuthOptions>(section);
var authOptions = section.Get<AuthOptions>()!;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.ApplyMigrationsAsync();

app.UseHttpsRedirection();
app.UseAuthentication();

await app.RunAsync();