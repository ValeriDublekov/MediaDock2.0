using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class OmdbDailyUsageConfiguration : IEntityTypeConfiguration<OmdbDailyUsage>
{
    public void Configure(EntityTypeBuilder<OmdbDailyUsage> builder)
    {
        builder.ToTable("omdb_daily_usage", table => table.HasCheckConstraint(
            "ck_omdb_daily_usage_counts",
            "total_requests >= 0 AND torrent_requests >= 0 AND oscar_requests >= 0 AND torrent_requests + oscar_requests <= total_requests"));
        builder.HasKey(entity => entity.UtcDate).HasName("pk_omdb_daily_usage");
        builder.Property(entity => entity.UtcDate).HasColumnName("utc_date").HasColumnType("date");
        builder.Property(entity => entity.TotalRequests).HasColumnName("total_requests").IsRequired();
        builder.Property(entity => entity.TorrentRequests)
            .HasColumnName("torrent_requests")
            .HasDefaultValue(0)
            .IsRequired();
        builder.Property(entity => entity.OscarRequests).HasColumnName("oscar_requests").IsRequired();
        builder.Property(entity => entity.ProviderQuotaExceeded)
            .HasColumnName("provider_quota_exceeded")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(entity => entity.DailyRequestLimitReached)
            .HasColumnName("daily_request_limit_reached")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(entity => entity.LastErrorCode)
            .HasColumnName("last_error_code")
            .HasMaxLength(64);
        builder.Property(entity => entity.LastProviderRequestAt).HasColumnName("last_provider_request_at");
        builder.Property(entity => entity.LastProviderSuccessAt).HasColumnName("last_provider_success_at");
        builder.Property(entity => entity.LastProviderErrorAt).HasColumnName("last_provider_error_at");
        builder.Property(entity => entity.ProviderQuotaExceededAt).HasColumnName("provider_quota_exceeded_at");
        builder.Property(entity => entity.QuotaRecoveryAttemptedAt).HasColumnName("quota_recovery_attempted_at");
        builder.Property(entity => entity.QuotaRecoverySucceededAt).HasColumnName("quota_recovery_succeeded_at");
    }
}