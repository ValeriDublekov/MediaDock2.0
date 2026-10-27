using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class RssFeedPreviewSnapshotConfiguration : IEntityTypeConfiguration<RssFeedPreviewSnapshot>
{
    public void Configure(EntityTypeBuilder<RssFeedPreviewSnapshot> builder)
    {
        builder.ToTable("rss_feed_preview_snapshots", table =>
            table.HasCheckConstraint(
                "ck_rss_feed_preview_snapshots_profile_id",
                "profile_id IN ('movie', 'series_complete', 'series_ongoing')"));
        builder.HasKey(entity => entity.Id).HasName("pk_rss_feed_preview_snapshots");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.ProfileId).HasColumnName("profile_id").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.CachedAt).HasColumnName("cached_at").IsRequired();
        builder.Property(entity => entity.FeedsJson).HasColumnName("feeds_json").HasColumnType("jsonb").IsRequired();
        builder.HasIndex(entity => new { entity.ProfileId, entity.CachedAt, entity.Id })
            .HasDatabaseName("ix_rss_feed_preview_snapshots_profile_cached_at_id");
    }
}