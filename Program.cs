using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using ShipmentService.Api;
using ShipmentService.Application;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.Carrier;
using ShipmentService.Infrastructure.Outbox;
using ShipmentService.Infrastructure.Persistence;
using ShipmentService.Infrastructure.Storage;
using ShipmentService.Infrastructure.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

builder.Services.AddDbContext<ShipmentDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("ShipmentDb"));
});

builder.Services.AddScoped<ShipmentCreationHandler>();
builder.Services.AddScoped<CarrierBookingProcessor>();
builder.Services.AddScoped<ShipmentCancellationService>();

builder.Services.AddScoped<IShipmentRepository, ShipmentRepository>();
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddSingleton<IMessagePublisher, LoggingMessagePublisher>();

builder.Services.AddSingleton<ILabelStorage, FileSystemLabelStorage>();

builder.Services
    .AddHttpClient<ICarrierShipmentClient, CarrierShipmentClient>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Services:CarrierService"]
            ?? throw new InvalidOperationException("Carrier Service URL is not configured"));

        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(4);
        options.Retry.DisableForUnsafeHttpMethods();
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
    });

builder.Services.AddHostedService<CarrierBookingWorker>();
builder.Services.AddHostedService<OutboxDispatcher>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShipmentDbContext>();

var app = builder.Build();

app.UseExceptionHandler();

app.MapHealthChecks("/health");
app.MapShipmentEndpoints();

app.Run();
