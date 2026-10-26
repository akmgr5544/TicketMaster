using Notifications.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNotificationsServices(builder.Configuration);
builder.Host.ConfigureMessaging(builder.Configuration);

var app = builder.Build();

await app.ApplyMigrationsAsync();

// No public API: the service only reacts to messages. This answers a liveness probe.
app.MapGet("/health", () => Results.Ok());

await app.RunAsync();

// Top-level statements generate an internal entry point; WebApplicationFactory<Program> needs a public one.
public partial class Program;
