using MediaDock.Application.Ingestion;
using MediaDock.Application.Metadata;
using MediaDock.Application.OscarAwards;
using MediaDock.Infrastructure.Metadata;
using MediaDock.Infrastructure.OscarAwards;
using MediaDock.Infrastructure.Persistence;
using MediaDock.Infrastructure.Rss;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaDock.Infrastructure.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IRssDnsResolver, SystemRssDnsResolver>();
        services.AddSingleton(serviceProvider =>
        {
            var dnsResolver = serviceProvider.GetRequiredService<IRssDnsResolver>();
            return new RssFeedTransport(RssFeedHttpClientFactory.Create(dnsResolver), dnsResolver);
        });
        services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        services.AddScoped<IngestionProviderSettings>();
        services.AddScoped<PostgresAdvisoryScanLock>();
        services.AddScoped<IRssIngestionRepository, PostgresRssIngestionRepository>();
        services.AddScoped<IMetadataCacheStore, PostgresMetadataCacheStore>();
        services.AddScoped<IOmdbRequestBudget, PostgresOmdbRequestBudget>();
        services.AddScoped<IOscarEnrichmentRepository, PostgresOscarEnrichmentRepository>();
        services.AddScoped<IOscarEnrichmentRunRepository, PostgresOscarEnrichmentRunRepository>();
        services.AddScoped<IRssFeedTransport, RssFeedTransportAdapter>();
        services.AddScoped<MetadataResolver>();
        services.AddScoped<RssIngestionService>();
        services.AddScoped<OscarEnrichmentService>();
        services.AddScoped<OscarDatasetImporter>();
        services.AddScoped<IOmdbClient>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IngestionProviderSettings>();
            return new OmdbClient(
                serviceProvider.GetRequiredService<HttpClient>(),
                settings.ApiKey ?? throw new InvalidOperationException("OMDb settings were not loaded for this job."),
                requestBudget: serviceProvider.GetRequiredService<IOmdbRequestBudget>(),
                dailyRequestLimit: settings.DailyRequestLimit,
                oscarDailyRequestLimit: settings.OscarDailyRequestLimit,
                timeProvider: serviceProvider.GetRequiredService<TimeProvider>());
        });

        return services;
    }
}