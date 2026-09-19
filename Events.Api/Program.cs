using Events.Api.Handlers;
using Events.Api.Rpc;
using Events.Application.Extensions;
using Events.Cosmos.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Handlers break the flow by throwing; this maps those throws to status codes.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<EventsExceptionHandler>();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplicationServices();

builder.Services.AddControllers();

// Needs an HTTP/2 endpoint. Run the https profile, where ALPN lets these calls share a port with the
// controllers. The plain http profile is HTTP/1.1 only and cannot serve them.
builder.Services.AddEventsRpc();

builder.Host.ConfigureRabbitMq(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.EnsureContainersAsync();

app.UseHttpsRedirection();

app.MapControllers();
app.MapGrpcService<EventsLookupService>();

await app.RunAsync();

// Top-level statements generate an internal entry point; WebApplicationFactory<Program> needs a
// public one. The EventsIntegration host fixture boots this Program unmodified.
public partial class Program;
