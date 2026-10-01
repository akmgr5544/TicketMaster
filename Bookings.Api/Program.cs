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

// MakeBooking names its GET action as nameof(GetBookingAsync); MVC strips the suffix by default, and then
// CreatedAtAction finds no route and answers 500 after the booking is already committed.
builder.Services.AddControllers(options => options.SuppressAsyncSuffixInActionNames = false);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Host.ConfigureRabbitMq(configuration);
builder.Services.AddIntegrationEventOutbox();

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

public partial class Program;
