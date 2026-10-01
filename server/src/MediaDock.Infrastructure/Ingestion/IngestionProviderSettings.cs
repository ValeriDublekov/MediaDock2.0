namespace MediaDock.Infrastructure.Ingestion;

public sealed class IngestionProviderSettings
{
    public string? ApiKey { get; private set; }
    public int DailyRequestLimit { get; private set; }
    public int OscarDailyRequestLimit { get; private set; }

    public void Configure(string apiKey, int dailyRequestLimit, int oscarDailyRequestLimit)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || dailyRequestLimit <= 0 || oscarDailyRequestLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dailyRequestLimit));
        }

        ApiKey = apiKey;
        DailyRequestLimit = dailyRequestLimit;
        OscarDailyRequestLimit = oscarDailyRequestLimit;
    }
}