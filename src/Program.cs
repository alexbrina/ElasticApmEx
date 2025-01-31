using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Formatting.Compact;
using System.Diagnostics;
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
        .Enrich.WithSpan()            // Add trace span enrichment
        .Enrich.WithMachineName()     // Add machine name enrichment
        .Enrich.WithEnvironmentName() // Add environment name enrichment

        // Add default sink
        .WriteTo.Console(new RenderedCompactJsonFormatter())
        ;
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
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
app.MapGet("/pids", () => JsonSerializer.Serialize( Process.GetProcesses().Select(p => new { p.Id, p.ProcessName }), jsonSerializerOptions));
app.MapGet("/exec", (string cmd) => ExecuteShellCommand(cmd));
app.MapGet("/logs", (ILogger<Program> logger) =>
{
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
