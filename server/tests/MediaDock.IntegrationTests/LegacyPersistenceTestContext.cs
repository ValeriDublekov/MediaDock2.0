using Microsoft.EntityFrameworkCore;

namespace MediaDock.IntegrationTests;

internal sealed class LegacyPersistenceTestContext(DbContextOptions<LegacyPersistenceTestContext> options)
    : DbContext(options)
{
    public DbSet<LegacyTitleRow> Titles => Set<LegacyTitleRow>();
    public DbSet<LegacyOscarFilmRow> OscarFilms => Set<LegacyOscarFilmRow>();
    public DbSet<LegacyGoldenGlobeGroupRow> GoldenGlobeGroups => Set<LegacyGoldenGlobeGroupRow>();
    public DbSet<LegacyOccurrenceRow> Occurrences => Set<LegacyOccurrenceRow>();
    public DbSet<LegacyFavoriteMovieRow> FavoriteMovies => Set<LegacyFavoriteMovieRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyTitleRow>(builder =>
        {
            builder.ToTable("titles");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
            builder.Property(entity => entity.TitleText).HasColumnName("title").IsRequired();
            builder.Property(entity => entity.NormalizedTitle).HasColumnName("normalized_title").IsRequired();
            builder.Property(entity => entity.Year).HasColumnName("year");
            builder.Property(entity => entity.MediaType).HasColumnName("media_type").IsRequired();
            builder.Property(entity => entity.SourceType).HasColumnName("source_type");
            builder.Property(entity => entity.ContentKind).HasColumnName("content_kind");
            builder.Property(entity => entity.BroadcastRangeStartYear).HasColumnName("broadcast_range_start_year");
            builder.Property(entity => entity.BroadcastRangeEndYear).HasColumnName("broadcast_range_end_year");
            builder.Property(entity => entity.BroadcastRangeRaw).HasColumnName("broadcast_range_raw");
            builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id");
            builder.Property(entity => entity.ImdbRating).HasColumnName("imdb_rating");
            builder.Property(entity => entity.ImdbVotes).HasColumnName("imdb_votes");
            builder.Property(entity => entity.Metascore).HasColumnName("metascore");
            builder.Property(entity => entity.Genres).HasColumnName("genres").HasColumnType("text[]");
            builder.Property(entity => entity.Countries).HasColumnName("countries").HasColumnType("text[]");
            builder.Property(entity => entity.Director).HasColumnName("director");
            builder.Property(entity => entity.Plot).HasColumnName("plot");
            builder.Property(entity => entity.PosterUrl).HasColumnName("poster_url");
            builder.Property(entity => entity.Runtime).HasColumnName("runtime");
            builder.Property(entity => entity.Awards).HasColumnName("awards");
            builder.Property(entity => entity.BoxOffice).HasColumnName("box_office");
            builder.Property(entity => entity.FirstSeenAt).HasColumnName("first_seen_at");
            builder.Property(entity => entity.LastSeenAt).HasColumnName("last_seen_at");
            builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<LegacyOscarFilmRow>(builder =>
        {
            builder.ToTable("oscar_films");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
            builder.Property(entity => entity.TitleId).HasColumnName("title_id");
            builder.Property(entity => entity.StableKey).HasColumnName("stable_key").IsRequired();
            builder.Property(entity => entity.FilmTitle).HasColumnName("film_title").IsRequired();
            builder.Property(entity => entity.NormalizedTitle).HasColumnName("normalized_title").IsRequired();
            builder.Property(entity => entity.FilmYear).HasColumnName("film_year");
            builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id");
            builder.Property(entity => entity.EnrichmentStatus).HasColumnName("enrichment_status").IsRequired();
            builder.Property(entity => entity.EnrichmentAttemptCount).HasColumnName("enrichment_attempt_count");
            builder.Property(entity => entity.LastEnrichmentAttemptAt).HasColumnName("last_enrichment_attempt_at");
            builder.Property(entity => entity.NextEnrichmentAttemptAt).HasColumnName("next_enrichment_attempt_at");
            builder.Property(entity => entity.LastEnrichmentError).HasColumnName("last_enrichment_error");
            builder.Property(entity => entity.ImportedAt).HasColumnName("imported_at");
            builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<LegacyGoldenGlobeGroupRow>(builder =>
        {
            builder.ToTable("golden_globe_groups");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
            builder.Property(entity => entity.Title).HasColumnName("title").IsRequired();
            builder.Property(entity => entity.Year).HasColumnName("year");
            builder.Property(entity => entity.NomineeType).HasColumnName("nominee_type").IsRequired();
            builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id");
            builder.Property(entity => entity.IsImdbIdManual).HasColumnName("is_imdb_id_manual");
            builder.Property(entity => entity.ImdbIdVersion).HasColumnName("imdb_id_version");
            builder.Property(entity => entity.EnrichmentStatus).HasColumnName("enrichment_status").IsRequired();
            builder.Property(entity => entity.EnrichmentAttemptCount).HasColumnName("enrichment_attempt_count");
            builder.Property(entity => entity.LastEnrichmentAttemptAt).HasColumnName("last_enrichment_attempt_at");
            builder.Property(entity => entity.NextEnrichmentAttemptAt).HasColumnName("next_enrichment_attempt_at");
            builder.Property(entity => entity.LastEnrichmentError).HasColumnName("last_enrichment_error");
        });

        modelBuilder.Entity<LegacyOccurrenceRow>(builder =>
        {
            builder.ToTable("occurrences");
            builder.HasKey(entity => entity.Id);
            builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
            builder.Property(entity => entity.TitleId).HasColumnName("title_id");
            builder.Property(entity => entity.SourceId).HasColumnName("source_id");
            builder.Property(entity => entity.SourceItemKey).HasColumnName("source_item_key").IsRequired();
            builder.Property(entity => entity.FeedEntryId).HasColumnName("feed_entry_id");
            builder.Property(entity => entity.TorrentUrl).HasColumnName("torrent_url").IsRequired();
            builder.Property(entity => entity.RawTitle).HasColumnName("raw_title").IsRequired();
            builder.Property(entity => entity.SourceFeedName).HasColumnName("source_feed_name").IsRequired();
            builder.Property(entity => entity.FeedType).HasColumnName("feed_type");
            builder.Property(entity => entity.SourcePublishedAt).HasColumnName("source_published_at");
            builder.Property(entity => entity.ObservedAt).HasColumnName("observed_at");
            builder.Property(entity => entity.Quality).HasColumnName("quality");
            builder.Property(entity => entity.RipType).HasColumnName("rip_type");
            builder.Property(entity => entity.FirstSeenAt).HasColumnName("first_seen_at");
            builder.Property(entity => entity.LastSeenAt).HasColumnName("last_seen_at");
        });

        modelBuilder.Entity<LegacyFavoriteMovieRow>(builder =>
        {
            builder.ToTable("favorite_movies");
            builder.HasKey(entity => new { entity.UserId, entity.TitleId });
            builder.Property(entity => entity.UserId).HasColumnName("user_id");
            builder.Property(entity => entity.TitleId).HasColumnName("title_id");
            builder.Property(entity => entity.ToWatch).HasColumnName("to_watch");
            builder.Property(entity => entity.ToDownload).HasColumnName("to_download");
            builder.Property(entity => entity.AddedFromOscar).HasColumnName("added_from_oscar");
            builder.Property(entity => entity.AddedFromCatalog).HasColumnName("added_from_catalog");
            builder.Property(entity => entity.CreatedAt).HasColumnName("created_at");
            builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        });
    }
}

internal sealed class LegacyTitleRow
{
    public long Id { get; set; }
    public string TitleText { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public string? ContentKind { get; set; }
    public int? BroadcastRangeStartYear { get; set; }
    public int? BroadcastRangeEndYear { get; set; }
    public string? BroadcastRangeRaw { get; set; }
    public string? ImdbId { get; set; }
    public decimal? ImdbRating { get; set; }
    public long? ImdbVotes { get; set; }
    public decimal? Metascore { get; set; }
    public string[] Genres { get; set; } = [];
    public string[] Countries { get; set; } = [];
    public string? Director { get; set; }
    public string? Plot { get; set; }
    public string? PosterUrl { get; set; }
    public string? Runtime { get; set; }
    public string? Awards { get; set; }
    public string? BoxOffice { get; set; }
    public DateTimeOffset? FirstSeenAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class LegacyOscarFilmRow
{
    public long Id { get; set; }
    public long TitleId { get; set; }
    public string StableKey { get; set; } = string.Empty;
    public string FilmTitle { get; set; } = string.Empty;
    public string NormalizedTitle { get; set; } = string.Empty;
    public int FilmYear { get; set; }
    public string? ImdbId { get; set; }
    public string EnrichmentStatus { get; set; } = "pending";
    public int EnrichmentAttemptCount { get; set; }
    public DateTimeOffset? LastEnrichmentAttemptAt { get; set; }
    public DateTimeOffset? NextEnrichmentAttemptAt { get; set; }
    public string? LastEnrichmentError { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class LegacyGoldenGlobeGroupRow
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string NomineeType { get; set; } = "movie";
    public string? ImdbId { get; set; }
    public bool IsImdbIdManual { get; set; }
    public int ImdbIdVersion { get; set; }
    public string EnrichmentStatus { get; set; } = "pending";
    public int EnrichmentAttemptCount { get; set; }
    public DateTimeOffset? LastEnrichmentAttemptAt { get; set; }
    public DateTimeOffset? NextEnrichmentAttemptAt { get; set; }
    public string? LastEnrichmentError { get; set; }
}

internal sealed class LegacyOccurrenceRow
{
    public long Id { get; set; }
    public long TitleId { get; set; }
    public long SourceId { get; set; }
    public string SourceItemKey { get; set; } = string.Empty;
    public string? FeedEntryId { get; set; }
    public string TorrentUrl { get; set; } = string.Empty;
    public string RawTitle { get; set; } = string.Empty;
    public string SourceFeedName { get; set; } = string.Empty;
    public string? FeedType { get; set; }
    public DateTimeOffset? SourcePublishedAt { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public string? Quality { get; set; }
    public string? RipType { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

internal sealed class LegacyFavoriteMovieRow
{
    public long UserId { get; set; }
    public long TitleId { get; set; }
    public bool ToWatch { get; set; }
    public bool ToDownload { get; set; }
    public bool AddedFromOscar { get; set; }
    public bool AddedFromCatalog { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}