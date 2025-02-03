using Elastic.Apm;
using Elastic.Apm.Api;
using Elastic.Apm.AspNetCore.DiagnosticListener;
using Elastic.Apm.DiagnosticSource;
using Elastic.Apm.Elasticsearch;
using Elastic.Apm.EntityFrameworkCore;
using Elastic.Apm.GrpcClient;
using Elastic.Apm.Instrumentations.SqlClient;
using Elastic.Apm.MongoDb;
using Elastic.Apm.SerilogEnricher;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Elasticsearch;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Webapi;

var builder = WebApplication.CreateBuilder(args);

// Cache JsonSerializerOptions instance
var jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };

// Configure Serilog
builder.Host.UseSerilog((context, config) =>
{
    config
        // Load configuration from appsettings.json
        .ReadFrom.Configuration(context.Configuration)

        // Add additional enrichers
        .Enrich.FromLogContext()
        .Enrich.WithSpan()                          // Add trace span enrichment
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithElasticApmCorrelationInfo()

        // Add default sink
        .WriteTo.Console(new RenderedCompactJsonFormatter())
        .WriteTo.Elasticsearch(new ElasticsearchSinkOptions(
            new Uri("http://localhost:9200"))
        {
            AutoRegisterTemplate = true,
            AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv8,
            IndexFormat = "dotnet-logs-{0:yyyy.MM.dd}",
            FailureCallback = e => Console.WriteLine("Unable to submit event " + e.MessageTemplate),
            EmitEventFailure = EmitEventFailureHandling.WriteToSelfLog |
                               EmitEventFailureHandling.WriteToFailureSink |
                               EmitEventFailureHandling.RaiseCallback
        });
});

builder.Services.AddElasticApm(
   new HttpDiagnosticsSubscriber(),
   new AspNetCoreDiagnosticSubscriber()
//    new EfCoreDiagnosticsSubscriber(),
//    new SqlClientDiagnosticSubscriber(),
//    new ElasticsearchDiagnosticsSubscriber(),
//    new GrpcClientDiagnosticSubscriber(),
//    new MongoDbDiagnosticsSubscriber()
   );

// Add other services
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

builder.Services.AddSingleton<ISellerMetrics, SellerMetrics>();

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => GetMsg());
app.MapGet("/env", (IConfiguration configuration) => JsonSerializer.Serialize(configuration.AsEnumerable(), jsonSerializerOptions));
app.MapGet("/pids", () => JsonSerializer.Serialize(Process.GetProcesses().Select(p => new { p.Id, p.ProcessName }), jsonSerializerOptions));
app.MapGet("/logs", (ILogger<Program> logger) =>
{
    var src = new ActivitySource("Test");
    using var activity = src.StartActivity("Custom-Activity-Logs");

    var msg = GetMsg();

    logger.LogTrace("TRACE: {Message}", msg);
    logger.LogDebug("DEBUG: {Message}", msg);
    logger.LogInformation("INFO: {Message}", msg);
    logger.LogWarning("WARN: {Message}", msg);
    logger.LogError("ERROR: {Message}", msg);

    return msg;
});
app.MapGet("/traces", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var src = new ActivitySource("Custom");
    using var activity = src.StartActivity("Custom-Activity-Fetch");
    activity?.AddTag("products_per_request", Random.Shared.Next(1, 1000));

    var client = httpClientFactory.CreateClient();
    var url = configuration["ServiceUrl"];
    var response = await client.GetAsync(url);
    var responseBody = await response.Content.ReadAsStringAsync();
    return new
    {
        Status = response.StatusCode,
        Body = responseBody
    };
});
app.MapGet("/metrics", async (HttpContext context, ISellerMetrics metrics) =>
{
    var startTime = DateTime.UtcNow;
    var clientId = Random.Shared.NextInt64(100000000, 100000010).ToString();
    var userAgent = context.Request.Headers.UserAgent.ToString();
    var totalItems = Random.Shared.Next(1, 1000);

    //using var memoryStream = new MemoryStream();
    //await context.Request.Body.CopyToAsync(memoryStream);
    //var requestSize = memoryStream.Length;
    var requestSize = Random.Shared.Next(1000, 10000);

    await Task.Delay(Random.Shared.Next(1, 50));

    var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

    var httpStatus = context.Response.StatusCode.ToString();
    var success = context.Response.StatusCode >= 200 && context.Response.StatusCode < 300;

    // Send metrics - Fire and forget!
    _ = metrics.AddIntegrationRequest(
        clientId,
        startTime,
        totalItems,
        requestSize,
        processingTimeMs,
        success,
        httpStatus,
        userAgent
    );

    return new
    {
        clientId,
        startTime,
        totalItems,
        requestSize,
        processingTimeMs,
        success,
        httpStatus,
        userAgent
    };
});

app.Run();

static string GetMsg() => $"This is {Environment.MachineName} # {Environment.ProcessId} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}";
