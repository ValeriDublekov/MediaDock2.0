using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("sources", table =>
            table.HasCheckConstraint(
                "ck_sources_feed_type",
                "feed_type IN ('movie', 'series_complete', 'series_ongoing')"));
        builder.HasKey(entity => entity.Id).HasName("pk_sources");
        builder.HasAlternateKey(entity => entity.StableKey).HasName("ak_sources_stable_key");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.StableKey).HasColumnName("stable_key").IsRequired();
        builder.Property(entity => entity.Name).HasColumnName("name").IsRequired();
        builder.Property(entity => entity.FeedType).HasColumnName("feed_type").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.Url).HasColumnName("url").IsRequired();
        builder.Property(entity => entity.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);
    }
}

internal sealed class TorrentMovieConfiguration : IEntityTypeConfiguration<TorrentMovie>
{
    public void Configure(EntityTypeBuilder<TorrentMovie> builder)
    {
        builder.ToTable("torrent_movies", table =>
        {
            table.HasCheckConstraint("ck_torrent_movies_media_type", "media_type IN ('movie', 'series', 'documentary', 'short')");
            table.HasCheckConstraint("ck_torrent_movies_source_type", "source_type IS NULL OR source_type IN ('movie', 'series')");
            table.HasCheckConstraint("ck_torrent_movies_content_kind", "content_kind IS NULL OR content_kind IN ('standard', 'documentary', 'short')");
            table.HasCheckConstraint(
                "ck_torrent_movies_seen_range",
                "(first_seen_at IS NULL AND last_seen_at IS NULL) OR (first_seen_at IS NOT NULL AND last_seen_at IS NOT NULL AND first_seen_at <= last_seen_at)");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_torrent_movies");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.TitleText).HasColumnName("title").IsRequired();
        builder.Property(entity => entity.NormalizedTitle).HasColumnName("normalized_title").IsRequired();
        builder.Property(entity => entity.Year).HasColumnName("year");
        builder.Property(entity => entity.MediaType).HasColumnName("media_type").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.SourceType).HasColumnName("source_type").HasMaxLength(16);
        builder.Property(entity => entity.ContentKind).HasColumnName("content_kind").HasMaxLength(16);
        builder.Property(entity => entity.BroadcastRangeStartYear).HasColumnName("broadcast_range_start_year");
        builder.Property(entity => entity.BroadcastRangeEndYear).HasColumnName("broadcast_range_end_year");
        builder.Property(entity => entity.BroadcastRangeRaw).HasColumnName("broadcast_range_raw");
        builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id").HasMaxLength(32);
        builder.Property(entity => entity.FirstSeenAt).HasColumnName("first_seen_at");
        builder.Property(entity => entity.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne(entity => entity.ImdbTitle)
            .WithMany()
            .HasForeignKey(entity => entity.ImdbId)
            .HasPrincipalKey(imdbTitle => imdbTitle.ImdbId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_torrent_movies_imdb_titles_imdb_id");
        builder.HasIndex(entity => entity.ImdbId)
            .IsUnique()
            .HasDatabaseName("ux_torrent_movies_imdb_id")
            .HasFilter("imdb_id IS NOT NULL AND btrim(imdb_id) <> ''");
        builder.HasIndex(entity => new { entity.NormalizedTitle, entity.Year, entity.MediaType })
            .HasDatabaseName("ix_torrent_movies_normalized_title_year_media_type");
    }
}

internal sealed class TorrentReleaseConfiguration : IEntityTypeConfiguration<TorrentRelease>
{
    public void Configure(EntityTypeBuilder<TorrentRelease> builder)
    {
        builder.ToTable("torrent_releases", table =>
            table.HasCheckConstraint("ck_torrent_releases_seen_range", "first_seen_at <= last_seen_at"));
        builder.HasKey(entity => entity.Id).HasName("pk_torrent_releases");
        builder.HasIndex(entity => new { entity.SourceId, entity.SourceItemKey })
            .IsUnique()
            .HasDatabaseName("ux_torrent_releases_source_id_source_item_key");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.TorrentMovieId).HasColumnName("torrent_movie_id");
        builder.Property(entity => entity.SourceId).HasColumnName("source_id");
        builder.Property(entity => entity.SourceItemKey).HasColumnName("source_item_key").IsRequired();
        builder.Property(entity => entity.FeedEntryId).HasColumnName("feed_entry_id");
        builder.Property(entity => entity.TorrentUrl).HasColumnName("torrent_url").IsRequired();
        builder.Property(entity => entity.RawTitle).HasColumnName("raw_title").IsRequired();
        builder.Property(entity => entity.SourceFeedName).HasColumnName("source_feed_name").IsRequired();
        builder.Property(entity => entity.FeedType).HasColumnName("feed_type").HasMaxLength(16);
        builder.Property(entity => entity.SourcePublishedAt).HasColumnName("source_published_at");
        builder.Property(entity => entity.ObservedAt).HasColumnName("observed_at");
        builder.Property(entity => entity.Quality).HasColumnName("quality");
        builder.Property(entity => entity.RipType).HasColumnName("rip_type");
        builder.Property(entity => entity.FirstSeenAt).HasColumnName("first_seen_at");
        builder.Property(entity => entity.LastSeenAt).HasColumnName("last_seen_at");
        builder.HasOne(entity => entity.TorrentMovie)
            .WithMany(movie => movie.Releases)
            .HasForeignKey(entity => entity.TorrentMovieId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_torrent_releases_movies_torrent_movie_id");
        builder.HasOne(entity => entity.Source)
            .WithMany()
            .HasForeignKey(entity => entity.SourceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_torrent_releases_sources_source_id");
        builder.HasIndex(entity => new { entity.TorrentMovieId, entity.LastSeenAt })
            .HasDatabaseName("ix_torrent_releases_torrent_movie_id_last_seen_at");
        builder.HasIndex(entity => entity.TorrentUrl)
            .IsUnique()
            .HasDatabaseName("ux_torrent_releases_torrent_url");
    }
}

internal sealed class RssItemProcessingStateConfiguration : IEntityTypeConfiguration<RssItemProcessingState>
{
    public void Configure(EntityTypeBuilder<RssItemProcessingState> builder)
    {
        builder.ToTable("rss_item_states", table =>
        {
            table.HasCheckConstraint("ck_rss_item_states_disposition", "disposition IN ('resolved', 'terminal')");
            table.HasCheckConstraint(
                "ck_rss_item_states_expiry",
                "disposition = 'resolved' OR (disposition = 'terminal' AND expires_at IS NOT NULL)");
        });
        builder.HasKey(entity => new { entity.SourceId, entity.SourceItemKey })
            .HasName("pk_rss_item_states");
        builder.Property(entity => entity.SourceId).HasColumnName("source_id");
        builder.Property(entity => entity.SourceItemKey).HasColumnName("source_item_key").IsRequired();
        builder.Property(entity => entity.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Disposition).HasColumnName("disposition").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at");
        builder.HasOne<Source>()
            .WithMany()
            .HasForeignKey(entity => entity.SourceId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_rss_item_states_sources_source_id");
        builder.HasIndex(entity => entity.ExpiresAt).HasDatabaseName("ix_rss_item_states_expires_at");
    }
}