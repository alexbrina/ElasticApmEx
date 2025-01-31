using Elastic.Apm.AspNetCore.DiagnosticListener;
using Elastic.Apm.DiagnosticSource;
using Elastic.Apm.SerilogEnricher;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Elasticsearch;
using System.Diagnostics;
using System.Text.Json;
using Webapi.Metrics;

var jsonSerializerOptions = new JsonSerializerOptions { WriteIndented = true };
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, config) =>
{
    config
        // Load configuration from appsettings.json
        .ReadFrom.Configuration(context.Configuration)

        // Add additional enrichers
        .Enrich.FromLogContext()
        .Enrich.WithSpan()                                              // Add trace span enrichment
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithElasticApmCorrelationInfo()                         // Correlate logs with APM traces

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

// Configure Elastic APM
builder.Services.AddElasticApm(
   new HttpDiagnosticsSubscriber(),
   new AspNetCoreDiagnosticSubscriber()
   );

// Add Application Metrics Service
builder.Services.AddSingleton<IApplicationMetrics, ElasticsearchMetrics>();

// Add other services
builder.Services.AddHttpClient();

// Configure Webapi Enpoints
var app = builder.Build();

app.MapGet("/", () => GetMsg());

app.MapGet("/logs", (ILogger<Program> logger) =>
{
    var src = new ActivitySource("Custom");                                 // custom activity source
    using var activity = src.StartActivity("Custom-Activity-Logs");         // custom activity

    var msg = GetMsg();

    logger.LogTrace("TRACE: {Message}", msg);
    logger.LogDebug("DEBUG: {Message}", msg);
    logger.LogInformation("INFO: {Message}", msg);
    logger.LogWarning("WARN: {Message}", msg);
    logger.LogError("ERROR: {Message}", msg);

    return "blessed logs generated!";
});

app.MapGet("/metrics", async (IApplicationMetrics metrics) =>
{
    var startTime = DateTimeOffset.UtcNow;
    await Task.Delay(Random.Shared.Next(1, 200));                                           // simulated processing time
    var clientId = Random.Shared.NextInt64(1000, 1010).ToString();                          // simulated clientId
    var totalItems = Random.Shared.Next(1, 1000);                                           // simulated total items
    var requestSize = Random.Shared.Next(1000, 10000);                                      // simulated request size
    var processingTimeMs = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;           // simulated processing time
    var success = Random.Shared.Next(1, 100) % 10 != 0;                                     // simulated operation success

    _ = metrics.AddPriceUpdate(new PriceUpdate                                              // send metrics - Fire and forget!
    {
        ClientId = clientId,
        Timestamp = startTime,
        ProcessingTimeMs = processingTimeMs,
        TotalItems = totalItems,
        SizeBytes = requestSize,
        Success = success
    });

    return "good metrics collected!";
});

app.MapGet("/traces", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
{
    var src = new ActivitySource("Custom");                                     // custom activity source
    using var activity = src.StartActivity("Custom-Activity-Fetch");            // custom activity

    var client = httpClientFactory.CreateClient();
    var url = configuration["ServiceUrl"];

    var response = await client.GetAsync(url);
    var responseBody = await response.Content.ReadAsStringAsync();

    activity?.AddTag("products_per_request", Random.Shared.Next(1, 1000));      // add a custom tag to the activity, appears as labels in APM

    return "useful traces provided!";
});

app.Run();

static string GetMsg() => $"This is {Environment.MachineName} # {Environment.ProcessId} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}";
