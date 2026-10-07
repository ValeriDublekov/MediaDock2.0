using MediaDock.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaDock.Infrastructure.Persistence.Configurations;

internal sealed class BackgroundJobConfiguration : IEntityTypeConfiguration<BackgroundJob>
{
    public void Configure(EntityTypeBuilder<BackgroundJob> builder)
    {
        builder.ToTable("background_jobs", table =>
        {
            table.HasCheckConstraint("ck_background_jobs_type", "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh')");
            table.HasCheckConstraint("ck_background_jobs_trigger", "trigger IN ('manual', 'schedule')");
            table.HasCheckConstraint("ck_background_jobs_status", "status IN ('queued', 'running', 'succeeded', 'partial', 'failed')");
            table.HasCheckConstraint(
                "ck_background_jobs_type_trigger",
                "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh') AND trigger = 'manual')");
            table.HasCheckConstraint(
                "ck_background_jobs_lifecycle",
                "(status = 'queued' AND started_at IS NULL AND finished_at IS NULL) OR "
                + "(status = 'running' AND started_at IS NOT NULL AND finished_at IS NULL) OR "
                + "(status IN ('succeeded', 'partial', 'failed') AND started_at IS NOT NULL AND finished_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_background_jobs_input_type",
                "(job_type IN ('oscar_import', 'golden_globe_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR "
                + "(job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh') AND input_bytes IS NULL)");
        });
        builder.HasKey(entity => entity.Id).HasName("pk_background_jobs");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.JobType).HasColumnName("job_type").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Trigger).HasColumnName("trigger").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.EnqueuedAt).HasColumnName("enqueued_at");
        builder.Property(entity => entity.StartedAt).HasColumnName("started_at");
        builder.Property(entity => entity.FinishedAt).HasColumnName("finished_at");
        builder.Property(entity => entity.ScheduledSlotUtc).HasColumnName("scheduled_slot_utc");
        builder.Property(entity => entity.ScanRunId).HasColumnName("scan_run_id");
        builder.Property(entity => entity.CurrentStage).HasColumnName("current_stage").HasMaxLength(64);
        builder.Property(entity => entity.CurrentSource).HasColumnName("current_source").HasMaxLength(200);
        builder.Property(entity => entity.ProgressUpdatedAt).HasColumnName("progress_updated_at");
        builder.Property(entity => entity.ErrorCode).HasColumnName("error_code").HasMaxLength(64);
        builder.Property(entity => entity.ResultSummary).HasColumnName("result_summary").HasColumnType("jsonb");
        builder.Property(entity => entity.InputFileName).HasColumnName("input_file_name").HasMaxLength(255);
        builder.Property(entity => entity.InputContentType).HasColumnName("input_content_type").HasMaxLength(128);
        builder.Property(entity => entity.InputBytes).HasColumnName("input_bytes");
        builder.HasOne(entity => entity.ScanRun)
            .WithMany()
            .HasForeignKey(entity => entity.ScanRunId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_background_jobs_scan_runs_scan_run_id");
        builder.HasIndex(entity => new { entity.Status, entity.EnqueuedAt })
            .HasDatabaseName("ix_background_jobs_status_enqueued_at");
        builder.HasIndex(entity => entity.ScheduledSlotUtc)
            .IsUnique()
            .HasFilter("scheduled_slot_utc IS NOT NULL")
            .HasDatabaseName("ux_background_jobs_scheduled_slot_utc");
        builder.HasIndex(entity => entity.ScanRunId)
            .IsUnique()
            .HasFilter("scan_run_id IS NOT NULL")
            .HasDatabaseName("ux_background_jobs_scan_run_id");
        builder.HasIndex(entity => entity.JobType, "ux_background_jobs_active_rss_scan")
            .IsUnique()
            .HasFilter("job_type = 'rss_scan' AND status IN ('queued', 'running')")
            .HasDatabaseName("ux_background_jobs_active_rss_scan");
        builder.HasIndex(entity => entity.JobType, "ux_background_jobs_active_oscar_enrichment")
            .IsUnique()
            .HasFilter("job_type = 'oscar_enrichment' AND status IN ('queued', 'running')")
            .HasDatabaseName("ux_background_jobs_active_oscar_enrichment");
        builder.HasIndex(entity => entity.JobType, "ux_background_jobs_active_golden_globe_enrichment")
            .IsUnique().HasFilter("job_type = 'golden_globe_enrichment' AND status IN ('queued', 'running')")
            .HasDatabaseName("ux_background_jobs_active_golden_globe_enrichment");
    }
}

internal sealed class BackgroundJobEventConfiguration : IEntityTypeConfiguration<BackgroundJobEvent>
{
    public void Configure(EntityTypeBuilder<BackgroundJobEvent> builder)
    {
        builder.ToTable("background_job_events");
        builder.HasKey(entity => entity.Id).HasName("pk_background_job_events");
        builder.Property(entity => entity.Id).UseIdentityByDefaultColumn().HasColumnName("id");
        builder.Property(entity => entity.JobId).HasColumnName("job_id");
        builder.Property(entity => entity.OccurredAt).HasColumnName("occurred_at");
        builder.Property(entity => entity.Level).HasColumnName("level").HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.EventCode).HasColumnName("event_code").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.DataJson).HasColumnName("data").HasColumnType("jsonb");
        builder.HasOne(entity => entity.Job)
            .WithMany(job => job.Events)
            .HasForeignKey(entity => entity.JobId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_background_job_events_background_jobs_job_id");
        builder.HasIndex(entity => new { entity.JobId, entity.Id })
            .HasDatabaseName("ix_background_job_events_job_id_id");
    }
}

internal sealed class BackgroundSchedulerStateConfiguration : IEntityTypeConfiguration<BackgroundSchedulerState>
{
    public void Configure(EntityTypeBuilder<BackgroundSchedulerState> builder)
    {
        builder.ToTable("background_scheduler_state");
        builder.HasKey(entity => entity.ScheduleName).HasName("pk_background_scheduler_state");
        builder.Property(entity => entity.ScheduleName).HasColumnName("schedule_name").HasMaxLength(64);
        builder.Property(entity => entity.LastEvaluatedSlotUtc).HasColumnName("last_evaluated_slot_utc");
    }
}
