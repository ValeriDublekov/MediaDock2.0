using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDock.Infrastructure.Persistence;

public sealed class MediaDockDbContext : DbContext
{
    public MediaDockDbContext(DbContextOptions<MediaDockDbContext> options)
        : base(options)
    {
    }

    public DbSet<ImdbTitle> ImdbTitles => Set<ImdbTitle>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<RssItemProcessingState> RssItemStates => Set<RssItemProcessingState>();
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();
    public DbSet<ParseLog> ParseLogs => Set<ParseLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<MetadataCacheEntry> MetadataCache => Set<MetadataCacheEntry>();
    public DbSet<RssFeedPreviewSnapshot> RssFeedPreviewSnapshots => Set<RssFeedPreviewSnapshot>();
    public DbSet<OmdbDailyUsage> OmdbDailyUsage => Set<OmdbDailyUsage>();
    public DbSet<OscarMovie> OscarMovies => Set<OscarMovie>();
    public DbSet<OscarNomination> OscarNominations => Set<OscarNomination>();
    public DbSet<OscarEnrichmentRun> OscarEnrichmentRuns => Set<OscarEnrichmentRun>();
    public DbSet<GoldenGlobeAward> GoldenGlobeAwards => Set<GoldenGlobeAward>();
    public DbSet<GoldenGlobeTitle> GoldenGlobeTitles => Set<GoldenGlobeTitle>();
    public DbSet<GoldenGlobeNomination> GoldenGlobeNominations => Set<GoldenGlobeNomination>();
    public DbSet<ImdbFavoriteMovie> ImdbFavoriteMovies => Set<ImdbFavoriteMovie>();
    public DbSet<UserIgnoredMovie> UserIgnoredMovies => Set<UserIgnoredMovie>();
    public DbSet<TorrentMovie> TorrentMovies => Set<TorrentMovie>();
    public DbSet<TorrentRelease> TorrentReleases => Set<TorrentRelease>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<BackgroundJobEvent> BackgroundJobEvents => Set<BackgroundJobEvent>();
    public DbSet<BackgroundSchedulerState> BackgroundSchedulerStates => Set<BackgroundSchedulerState>();
    public DbSet<PersonalRating> PersonalRatings => Set<PersonalRating>();
    public DbSet<GeneralRecommendationSettings> GeneralRecommendationSettings => Set<GeneralRecommendationSettings>();
    public DbSet<AwardCategoryAlias> AwardCategoryAliases => Set<AwardCategoryAlias>();
    public DbSet<RecommendationAwardWeight> RecommendationAwardWeights => Set<RecommendationAwardWeight>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<RegistrationRequest> RegistrationRequests => Set<RegistrationRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaDockDbContext).Assembly);
    }
}
