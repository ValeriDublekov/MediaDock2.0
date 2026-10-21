using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class GoldenGlobeAwardConfiguration : IEntityTypeConfiguration<GoldenGlobeAward>
{
    public void Configure(EntityTypeBuilder<GoldenGlobeAward> builder)
    {
        builder.ToTable("golden_globe_awards");
        builder.HasKey(x => x.Id).HasName("pk_golden_globe_awards");
        builder.Property(x => x.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_golden_globe_awards_name");
    }
}

internal sealed class GoldenGlobeTitleConfiguration : IEntityTypeConfiguration<GoldenGlobeTitle>
{
    public void Configure(EntityTypeBuilder<GoldenGlobeTitle> builder)
    {
        builder.ToTable("golden_globe_titles", table =>
        {
            table.HasCheckConstraint(
                "ck_golden_globe_titles_enrichment_status",
                "enrichment_status IN ('pending', 'enriched', 'problem', 'not_found', 'temporary_error')");
            table.HasCheckConstraint(
                "ck_golden_globe_titles_enrichment_attempt_count",
                "enrichment_attempt_count >= 0");
            table.HasCheckConstraint("ck_golden_globe_titles_nominee_type", "nominee_type IN ('movie', 'series')");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_golden_globe_titles");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.Title).HasColumnName("title").HasMaxLength(1000).IsRequired();
        builder.Property(entity => entity.Year).HasColumnName("year");
        builder.Property(entity => entity.NomineeType).HasColumnName("nominee_type").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.ImdbId).HasColumnName("imdb_id").HasMaxLength(32);
        builder.Property(entity => entity.IsImdbIdManual).HasColumnName("is_imdb_id_manual").HasDefaultValue(false).IsRequired();
        builder.Property(entity => entity.ImdbIdVersion).HasColumnName("imdb_id_version").HasDefaultValue(0).IsRequired();
        builder.Property(entity => entity.EnrichmentStatus).HasColumnName("enrichment_status").HasMaxLength(24).IsRequired();
        builder.Property(entity => entity.EnrichmentAttemptCount).HasColumnName("enrichment_attempt_count");
        builder.Property(entity => entity.LastEnrichmentAttemptAt).HasColumnName("last_enrichment_attempt_at");
        builder.Property(entity => entity.NextEnrichmentAttemptAt).HasColumnName("next_enrichment_attempt_at");
        builder.Property(entity => entity.LastEnrichmentError).HasColumnName("last_enrichment_error");
        builder.HasIndex(entity => new { entity.Title, entity.Year, entity.NomineeType })
            .IsUnique()
            .HasDatabaseName("ux_golden_globe_titles_identity");
        builder.HasIndex(entity => entity.ImdbId).HasDatabaseName("ix_golden_globe_titles_imdb_id");
        builder.HasOne(entity => entity.ImdbTitle)
            .WithMany()
            .HasForeignKey(entity => entity.ImdbId)
            .HasPrincipalKey(imdbTitle => imdbTitle.ImdbId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_golden_globe_titles_imdb_titles_imdb_id");
    }
}

internal sealed class GoldenGlobeNominationConfiguration : IEntityTypeConfiguration<GoldenGlobeNomination>
{
    public void Configure(EntityTypeBuilder<GoldenGlobeNomination> builder)
    {
        builder.ToTable("golden_globe_nominations");
        builder.HasKey(x => x.Id).HasName("pk_golden_globe_nominations");
        builder.Property(x => x.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(x => x.ImportKey).HasColumnName("import_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Winner).HasColumnName("winner");
        builder.Property(x => x.AwardId).HasColumnName("award_id");
        builder.Property(x => x.GoldenGlobeTitleId).HasColumnName("golden_globe_title_id");
        builder.HasIndex(x => x.ImportKey).IsUnique().HasDatabaseName("ux_golden_globe_nominations_import_key");
        builder.HasOne(x => x.Award).WithMany(x => x.Nominations).HasForeignKey(x => x.AwardId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_golden_globe_nominations_awards_award_id");
        builder.HasOne(x => x.GoldenGlobeTitle).WithMany(x => x.Nominations).HasForeignKey(x => x.GoldenGlobeTitleId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_golden_globe_nominations_titles_title_id");
    }
}
