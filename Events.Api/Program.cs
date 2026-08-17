using Events.Api.Handlers;
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

builder.Host.ConfigureRabbitMq();

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

await app.RunAsync();
