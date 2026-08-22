using Bookings.Api.Handlers;
using Bookings.Application.Extensions;
using Bookings.Sql.Extensions;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Handlers break the flow by throwing; this maps those throws to status codes.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BookingsExceptionHandler>();

builder.Services.AddInfrastructureServices(configuration);
builder.Services.AddApplicationServices(configuration);

builder.Services.AddControllers();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Host.ConfigureRabbitMq(configuration);

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.ApplyMigrationsAsync();

app.UseHttpsRedirection();

app.MapControllers();

await app.RunAsync();