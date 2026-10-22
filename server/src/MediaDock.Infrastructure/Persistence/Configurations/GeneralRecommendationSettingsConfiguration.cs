using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class GeneralRecommendationSettingsConfiguration : IEntityTypeConfiguration<GeneralRecommendationSettings>
{
    public void Configure(EntityTypeBuilder<GeneralRecommendationSettings> builder)
    {
        builder.ToTable("general_recommendation_settings", table =>
        {
            table.HasCheckConstraint("ck_general_recommendation_settings_singleton_id", "id = 1");
            table.HasCheckConstraint("ck_general_recommendation_settings_median_period_years", "median_period_years > 0");
            table.HasCheckConstraint(
                "ck_general_recommendation_settings_requires_vote_recalculation",
                "requires_vote_recalculation IN (TRUE, FALSE)");
            table.HasCheckConstraint(
                "ck_general_recommendation_settings_last_vote_recalculation_period_years",
                "last_vote_recalculation_period_years IS NULL OR last_vote_recalculation_period_years > 0");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_general_recommendation_settings");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.MedianPeriodYears)
            .HasColumnName("median_period_years")
            .HasDefaultValue(5);
        builder.Property(entity => entity.RequiresVoteRecalculation)
            .HasColumnName("requires_vote_recalculation")
            .HasDefaultValue(false);
        builder.Property(entity => entity.LastVoteRecalculationAt).HasColumnName("last_vote_recalculation_at");
        builder.Property(entity => entity.LastVoteRecalculationPeriodYears)
            .HasColumnName("last_vote_recalculation_period_years");
    }
}