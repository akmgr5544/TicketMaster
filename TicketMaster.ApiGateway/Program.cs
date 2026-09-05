using Microsoft.AspNetCore.Authentication;
using TicketMaster.ApiGateway.Handlers;
using TicketMaster.ApiGateway.Transforms;

var builder = WebApplication.CreateBuilder(args);

var usersServiceAddress = builder.Configuration["Services:Users:BaseAddress"]
                          ?? throw new InvalidOperationException(
                              "'Services:Users:BaseAddress' is not configured.");

builder.Services.AddHttpClient("UsersService", config =>
{
    config.BaseAddress = new Uri(usersServiceAddress);
});

builder.Services.AddAuthentication("UserServiceScheme")
    .AddScheme<AuthenticationSchemeOptions, UsersServiceAuthHandler>("UserServiceScheme", null);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("GatewayAuthPolicy", policy => policy.RequireAuthenticatedUser());
});

builder.Configuration
    .AddJsonFile("YarpConfigurations/yarp.clusters.json", optional: false, reloadOnChange: true)
    .AddJsonFile("YarpConfigurations/yarp.routes.json", optional: false, reloadOnChange: true);

var config = builder.Configuration.GetSection("ReverseProxy");
builder.Services.AddReverseProxy()
    .LoadFromConfig(config)
    .AddTransforms<AuthTransformProvider>();

var app = builder.Build();

app.MapReverseProxy();
await app.RunAsync();