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

internal sealed class GoldenGlobeNominationConfiguration : IEntityTypeConfiguration<GoldenGlobeNomination>
{
    public void Configure(EntityTypeBuilder<GoldenGlobeNomination> builder)
    {
        builder.ToTable("golden_globe_nominations", table =>
        {
            table.HasCheckConstraint(
                "ck_golden_globe_nominations_enrichment_status",
                "enrichment_status IN ('pending', 'enriched', 'problem', 'not_found', 'temporary_error')");
            table.HasCheckConstraint(
                "ck_golden_globe_nominations_enrichment_attempt_count",
                "enrichment_attempt_count >= 0");
            table.HasCheckConstraint(
                "ck_golden_globe_nominations_nominee_type",
                "nominee_type IN ('movie', 'series')");
        });
        builder.HasKey(x => x.Id).HasName("pk_golden_globe_nominations");
        builder.Property(x => x.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(x => x.ImportKey).HasColumnName("import_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Year).HasColumnName("year");
        builder.Property(x => x.Winner).HasColumnName("winner");
        builder.Property(x => x.AwardId).HasColumnName("award_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.NomineeType).HasColumnName("nominee_type").HasMaxLength(16).IsRequired();
        builder.Property(x => x.ImdbId).HasColumnName("imdb_id").HasMaxLength(32);
        builder.Property(x => x.IsImdbIdManual).HasColumnName("is_imdb_id_manual").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ImdbIdVersion).HasColumnName("imdb_id_version").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.EnrichmentStatus).HasColumnName("enrichment_status").HasMaxLength(24).IsRequired();
        builder.Property(x => x.EnrichmentAttemptCount).HasColumnName("enrichment_attempt_count");
        builder.Property(x => x.LastEnrichmentAttemptAt).HasColumnName("last_enrichment_attempt_at");
        builder.Property(x => x.NextEnrichmentAttemptAt).HasColumnName("next_enrichment_attempt_at");
        builder.Property(x => x.LastEnrichmentError).HasColumnName("last_enrichment_error");
        builder.HasIndex(x => x.ImportKey).IsUnique().HasDatabaseName("ux_golden_globe_nominations_import_key");
        builder.HasIndex(x => new { x.Year, x.AwardId }).HasDatabaseName("ix_golden_globe_nominations_year_award");
        builder.HasOne(x => x.Award).WithMany(x => x.Nominations).HasForeignKey(x => x.AwardId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_golden_globe_nominations_awards_award_id");
    }
}
