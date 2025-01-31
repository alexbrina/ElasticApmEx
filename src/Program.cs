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
app.MapGet("/exec", (string cmd) => ExecuteShellCommand(cmd));
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
app.MapGet("/fetch", async (IHttpClientFactory httpClientFactory, IConfiguration configuration) =>
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

app.Run();

static string GetMsg() => $"This is {Environment.MachineName} # {Environment.ProcessId} @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}";

static string ExecuteShellCommand(string command)
{
    try
    {
        // Create a new process
        using (Process process = new())
        {
            // Set up process start info
            process.StartInfo.FileName = "/bin/bash";
            process.StartInfo.Arguments = $"-c \"{command}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            // Start the process
            process.Start();

            // Read the output and error streams
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            // Wait for the process to finish
            process.WaitForExit();

            // Return output or error
            return string.IsNullOrEmpty(error) ? output : error;
        }
    }
    catch (Exception ex)
    {
        return $"An error occurred: {ex.Message}";
    }
}
