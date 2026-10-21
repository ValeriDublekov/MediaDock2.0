using MediaDock.Application.Metadata;

namespace MediaDock.Infrastructure.Metadata;

public sealed class OmdbClientFactory(
    HttpClient httpClient,
    IOmdbRequestBudget requestBudget,
    TimeProvider timeProvider) : IOmdbClientFactory
{
    public IOmdbClient Create(string apiKey, int dailyRequestLimit) => new OmdbClient(
        httpClient,
        apiKey,
        requestBudget: requestBudget,
        dailyRequestLimit: dailyRequestLimit,
        timeProvider: timeProvider);
}