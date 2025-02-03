using Nest;
using System.Collections.Concurrent;

namespace Webapi;

public class SellerBulkMetrics : ISellerMetrics, IDisposable
{
    private const int MaxQueueSize = 10_000;
    private readonly BlockingCollection<object> _queue = new(MaxQueueSize);
    private readonly ElasticClient _elasticClient;
    private readonly ILogger<SellerMetrics> _logger;
    private readonly int _bulkSize;
    private readonly TimeSpan _flushInterval;

    public SellerBulkMetrics(
        IConfiguration configuration,
        ILogger<SellerMetrics> logger)
    {
        _logger = logger;
        var settings = new ConnectionSettings(new Uri(configuration["Elasticsearch:Uri"]))
            .DefaultIndex("logs-default");

        _elasticClient = new ElasticClient(settings);
        _bulkSize = configuration.GetValue<int>("Elasticsearch:BulkSize", 1000);
        _flushInterval = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Elasticsearch:FlushIntervalSeconds", 5));

        StartProcessingTask();
    }

    public Task AddIntegrationRequest(
        string clientId,
        DateTime timestamp,
        int totalItems,
        long sizeBytes,
        double processingTimeMs,
        bool success,
        string httpStatus,
        string userAgent)
    {
        var logEntry = new
        {
            timestamp,
            client_id = clientId,
            request_metrics = new
            {
                total_items = totalItems,
                size_bytes = sizeBytes,
                processing_time_ms = processingTimeMs
            },
            success,
            http_status = httpStatus,
            user_agent = userAgent
        };

        if (!_queue.TryAdd(logEntry))
        {
            _logger.LogWarning("Metrics queue is full - dropping metric entry");
        }

        return Task.CompletedTask;
    }

    private void StartProcessingTask()
    {
        Task.Run(async () =>
        {
            var buffer = new List<object>(_bulkSize);
            var lastFlush = DateTime.UtcNow;

            while (!_queue.IsCompleted)
            {
                try
                {
                    if (_queue.TryTake(out var item, 100))
                    {
                        buffer.Add(item);
                    }

                    var shouldFlush = buffer.Count >= _bulkSize ||
                                    (DateTime.UtcNow - lastFlush) >= _flushInterval;

                    if (shouldFlush && buffer.Count > 0)
                    {
                        await ProcessBuffer(buffer);
                        buffer.Clear();
                        lastFlush = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing metrics queue");
                    await Task.Delay(1000);
                }
            }
        });
    }

    private async Task ProcessBuffer(List<object> buffer)
    {
        try
        {
            var bulkResponse = await _elasticClient.BulkAsync(b =>
            {
                foreach (var item in buffer)
                {
                    b.Index<object>(io => io
                        .Document(item)
                        .Index("logs-default")
                    );
                }
                return b;
            });

            if (!bulkResponse.IsValid)
            {
                _logger.LogError("Bulk index error: {DebugInformation}",
                    bulkResponse.DebugInformation);
            }
            else if (bulkResponse.ItemsWithErrors.Any())
            {
                _logger.LogError("Bulk index partial failure: {ErrorCount} failures",
                    bulkResponse.ItemsWithErrors.Count());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process metrics batch");
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        var remaining = _queue.Count;
        if (remaining > 0)
        {
            _logger.LogInformation("Flushing {Count} remaining metrics", remaining);
            ProcessBuffer(_queue.ToList()).Wait();
        }
        _queue.Dispose();
    }
}
