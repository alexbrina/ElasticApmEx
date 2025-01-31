namespace Webapi.Metrics;

public interface IApplicationMetrics
{
    Task<int> AddPriceUpdate(PriceUpdate data);
}
