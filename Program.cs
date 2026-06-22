using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using ShipmentService.Api;
using ShipmentService.Application;
using ShipmentService.Application.Ports;
using ShipmentService.Infrastructure.Carrier;
using ShipmentService.Infrastructure.FeatureFlags;
using ShipmentService.Infrastructure.Messaging;
using ShipmentService.Infrastructure.Outbox;
using ShipmentService.Infrastructure.Persistence;
using ShipmentService.Infrastructure.Storage;
using ShipmentService.Infrastructure.Workers;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Environment.ApplicationName;
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:5107";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ShipmentFeatureFlags>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
var featureFlags = builder.Configuration.GetSection("FeatureFlags").Get<ShipmentFeatureFlags>() ?? new ShipmentFeatureFlags();

builder.Services.AddDbContext<ShipmentDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("ShipmentDb"));
});

builder.Services.AddScoped<ShipmentCreationHandler>();
builder.Services.AddScoped<CarrierBookingProcessor>();
builder.Services.AddScoped<ShipmentCancellationService>();

if (featureFlags.UseMockShipmentRepository)
{
    builder.Services.AddScoped<IShipmentRepository, MockShipmentRepository>();
}
else
{
    builder.Services.AddScoped<IShipmentRepository, ShipmentRepository>();
}
builder.Services.AddScoped<IOutboxWriter, OutboxWriter>();
builder.Services.AddSingleton<IMessagePublisher, KafkaMessagePublisher>();

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
        //options.Retry.DisableForUnsafeHttpMethods();
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 10;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
    });

builder.Services.AddHostedService<CarrierBookingWorker>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<OrderCreatedKafkaConsumer>();
builder.Services.AddHostedService<ShipmentCommandsConsumer>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShipmentDbContext>();

var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint("/metrics");

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapShipmentEndpoints();

app.Run();
