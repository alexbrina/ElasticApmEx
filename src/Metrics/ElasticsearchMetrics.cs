using Elastic.Clients.Elasticsearch;

namespace Webapi.Metrics;

// ElasticsearchMetrics should be Singleton as the ElasticsearchClient is thread-safe and can
// be shared and reused across multiple threads. Of course, no other shared state should be
// modified, use only method scoped variables.
public class ElasticsearchMetrics : IApplicationMetrics
{
    public const int SUCCESS = 0;
    public const int INVALID_RESPONSE = 1;
    public const int EXCEPTION_OCCURRED = 2;

    private static readonly string environment = SetupEnvironmentName();
    private static readonly string prefix = "seller";

    private readonly ElasticsearchClient client;
    private readonly ILogger<ElasticsearchMetrics> logger;

    public ElasticsearchMetrics(IConfiguration configuration, ILogger<ElasticsearchMetrics> logger)
    {
        var uri = configuration["Elasticsearch:Uri"];
        var settings = new ElasticsearchClientSettings(uri is null ? null : new Uri(uri));
        client = new ElasticsearchClient(settings);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> AddPriceUpdate(PriceUpdate data)
    {
        try
        {
            var index = GetIndexName("price.update");
            var response = await client.IndexAsync(data, index: index);
            if (!response.IsValidResponse)
            {
                logger.LogWarning("Failed to send metric {MetricName} to index {ElasticsearchIndex} with message " +
                    "{ElasticsearchResponse}", nameof(PriceUpdate), index, response);
                return INVALID_RESPONSE;
            }

            return SUCCESS;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred trying to send metric {MetricName} to Elasticsearch", nameof(PriceUpdate));
            return EXCEPTION_OCCURRED;
        }
    }

    /// <summary>
    /// Rules for index names:
    /// 
    /// * Lowercase only
    /// * Cannot include \, /, *, ?, ", <, >, |, :, space (the character, not the word), ,, #
    /// * Cannot start with -, _, +
    /// * Cannot be.or..
    /// * Cannot be longer than 255 characters
    /// 
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private static string GetIndexName(string name) => $"{prefix}-{name}-{environment}-{DateTime.Now:yyyy.MM.dd}";

    private static string SetupEnvironmentName()
    {
        var value = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            value = "Production";
        }

        return value.ToLowerInvariant();
    }
}
