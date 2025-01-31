namespace Webapi.Metrics;

public class PriceUpdate
{
    public required string ClientId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public double ProcessingTimeMs { get; set; }
    public int TotalItems { get; set; }
    public long SizeBytes { get; set; }
    public bool Success { get; set; }
}
