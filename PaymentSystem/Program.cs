using PaymentSystem.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddFeatureEndpoints();
builder.Host.ConfigureMessaging(builder.Configuration);

var app = builder.Build();

await app.ApplyMigrationsAsync();

app.UseExceptionHandler();
app.MapFeatureEndpoints();

await app.RunAsync();

// Top-level statements generate an internal entry point; WebApplicationFactory<Program> needs a public one.
public partial class Program;
