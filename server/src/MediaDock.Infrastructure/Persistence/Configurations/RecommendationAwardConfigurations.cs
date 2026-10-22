using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class AwardCategoryAliasConfiguration : IEntityTypeConfiguration<AwardCategoryAlias>
{
    public void Configure(EntityTypeBuilder<AwardCategoryAlias> builder)
    {
        builder.ToTable("award_category_aliases", table =>
        {
            table.HasCheckConstraint(
                "ck_award_category_aliases_source",
                "award_source IN ('oscar', 'golden_globes')");
            table.HasCheckConstraint(
                "ck_award_category_aliases_names",
                "btrim(raw_name) <> '' AND btrim(category_key) <> ''");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_award_category_aliases");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.AwardSource)
            .HasColumnName("award_source")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(entity => entity.RawName)
            .HasColumnName("raw_name")
            .HasMaxLength(500)
            .IsRequired();
        builder.Property(entity => entity.CategoryKey)
            .HasColumnName("category_key")
            .HasMaxLength(128)
            .IsRequired();
        builder.HasIndex(entity => new { entity.AwardSource, entity.RawName })
            .IsUnique()
            .HasDatabaseName("ux_award_category_aliases_source_raw_name");
    }
}

internal sealed class RecommendationAwardWeightConfiguration : IEntityTypeConfiguration<RecommendationAwardWeight>
{
    public void Configure(EntityTypeBuilder<RecommendationAwardWeight> builder)
    {
        builder.ToTable("recommendation_award_weights", table =>
        {
            table.HasCheckConstraint(
                "ck_recommendation_award_weights_source",
                "award_source IN ('oscar', 'golden_globes')");
            table.HasCheckConstraint(
                "ck_recommendation_award_weights_category_key",
                "btrim(category_key) <> ''");
            table.HasCheckConstraint("ck_recommendation_award_weights_win_points", "win_points >= 0");
            table.HasCheckConstraint(
                "ck_recommendation_award_weights_nomination_points",
                "nomination_points >= 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_recommendation_award_weights");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.AwardSource)
            .HasColumnName("award_source")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(entity => entity.CategoryKey)
            .HasColumnName("category_key")
            .HasMaxLength(128)
            .IsRequired();
        builder.Property(entity => entity.WinPoints).HasColumnName("win_points").HasColumnType("numeric");
        builder.Property(entity => entity.NominationPoints)
            .HasColumnName("nomination_points")
            .HasColumnType("numeric");
        builder.HasIndex(entity => new { entity.AwardSource, entity.CategoryKey })
            .IsUnique()
            .HasDatabaseName("ux_recommendation_award_weights_source_category");
    }
}