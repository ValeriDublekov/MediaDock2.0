using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class ImdbTitleConfiguration : IEntityTypeConfiguration<ImdbTitle>
{
    public void Configure(EntityTypeBuilder<ImdbTitle> builder)
    {
        builder.ToTable("imdb_titles", table =>
        {
            table.HasCheckConstraint("ck_imdb_titles_imdb_id_normalized", "imdb_id <> '' AND imdb_id = lower(btrim(imdb_id))");
            table.HasCheckConstraint(
                "ck_imdb_titles_metadata_status",
                "metadata_status IN ('pending', 'enriched', 'not_found', 'temporary_error')");
            table.HasCheckConstraint("ck_imdb_titles_enrichment_attempt_count", "enrichment_attempt_count >= 0");
        });
        builder.HasKey(entity => entity.ImdbId).HasName("pk_imdb_titles");
        builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.TitleText).HasColumnName("title");
        builder.Property(entity => entity.Year).HasColumnName("year");
        builder.Property(entity => entity.MediaType).HasColumnName("media_type").HasMaxLength(32);
        builder.Property(entity => entity.ImdbRating).HasColumnName("imdb_rating").HasPrecision(4, 1);
        builder.Property(entity => entity.ImdbVotes).HasColumnName("imdb_votes");
        builder.Property(entity => entity.Metascore).HasColumnName("metascore").HasPrecision(5, 2);
        builder.Property(entity => entity.Genres)
            .HasColumnName("genres")
            .HasColumnType("text[]")
            .HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(entity => entity.Countries)
            .HasColumnName("countries")
            .HasColumnType("text[]")
            .HasDefaultValueSql("ARRAY[]::text[]");
        builder.Property(entity => entity.Director).HasColumnName("director");
        builder.Property(entity => entity.Plot).HasColumnName("plot");
        builder.Property(entity => entity.PosterUrl).HasColumnName("poster_url");
        builder.Property(entity => entity.Runtime).HasColumnName("runtime");
        builder.Property(entity => entity.Awards).HasColumnName("awards");
        builder.Property(entity => entity.BoxOffice).HasColumnName("box_office");
        builder.Property(entity => entity.MetadataStatus)
            .HasColumnName("metadata_status")
            .HasMaxLength(24)
            .HasDefaultValue("pending")
            .IsRequired();
        builder.Property(entity => entity.EnrichmentAttemptCount)
            .HasColumnName("enrichment_attempt_count")
            .HasDefaultValue(0);
        builder.Property(entity => entity.LastEnrichmentAttemptAt).HasColumnName("last_enrichment_attempt_at");
        builder.Property(entity => entity.NextEnrichmentAttemptAt).HasColumnName("next_enrichment_attempt_at");
        builder.Property(entity => entity.LastEnrichmentError).HasColumnName("last_enrichment_error");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}