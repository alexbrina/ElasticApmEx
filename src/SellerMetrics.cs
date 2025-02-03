using Nest;

namespace Webapi;
public interface ISellerMetrics
{
    Task AddIntegrationRequest(
        string clientId,
        DateTime timestamp,
        int totalItems,
        long sizeBytes,
        double processingTimeMs,
        bool success,
        string httpStatus,
        string userAgent);
}

public class SellerMetrics : ISellerMetrics
{
    private readonly ElasticClient _elasticClient;

    public SellerMetrics(IConfiguration configuration)
    {
        var settings = new ConnectionSettings(new Uri(configuration["Elasticsearch:Uri"]))
            .DefaultIndex("seller-integration");

        _elasticClient = new ElasticClient(settings);
    }

    public async Task AddIntegrationRequest(
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

        await _elasticClient.IndexDocumentAsync(logEntry);
    }
}
