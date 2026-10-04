using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MediaDock.Infrastructure.Persistence.Migrations;

[Migration("20261004200000_AddGoldenGlobeEnrichment")]
public partial class AddGoldenGlobeEnrichment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("enrichment_status", "golden_globe_nominations", maxLength: 24, nullable: false, defaultValue: "pending");
        migrationBuilder.AddColumn<int>("enrichment_attempt_count", "golden_globe_nominations", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<DateTimeOffset>("last_enrichment_attempt_at", "golden_globe_nominations", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("next_enrichment_attempt_at", "golden_globe_nominations", nullable: true);
        migrationBuilder.AddColumn<string>("last_enrichment_error", "golden_globe_nominations", nullable: true);
        migrationBuilder.AddCheckConstraint("ck_golden_globe_nominations_enrichment_status", "golden_globe_nominations", "enrichment_status IN ('pending', 'enriched', 'problem', 'not_found', 'temporary_error')");
        migrationBuilder.AddCheckConstraint("ck_golden_globe_nominations_enrichment_attempt_count", "golden_globe_nominations", "enrichment_attempt_count >= 0");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type", "background_jobs", "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment')");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type_trigger", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type_trigger", "background_jobs", "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment') AND trigger = 'manual')");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_input_type", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_input_type", "background_jobs", "(job_type IN ('oscar_import', 'golden_globe_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment') AND input_bytes IS NULL)");
        migrationBuilder.CreateIndex("ux_background_jobs_active_golden_globe_enrichment", "background_jobs", "job_type", unique: true, filter: "job_type = 'golden_globe_enrichment' AND status IN ('queued', 'running')");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ux_background_jobs_active_golden_globe_enrichment", "background_jobs");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_input_type", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_input_type", "background_jobs", "(job_type IN ('oscar_import', 'golden_globe_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment') AND input_bytes IS NULL)");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type_trigger", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type_trigger", "background_jobs", "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'oscar_enrichment') AND trigger = 'manual')");
        migrationBuilder.DropCheckConstraint("ck_background_jobs_type", "background_jobs");
        migrationBuilder.AddCheckConstraint("ck_background_jobs_type", "background_jobs", "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'oscar_enrichment')");
        migrationBuilder.DropCheckConstraint("ck_golden_globe_nominations_enrichment_attempt_count", "golden_globe_nominations");
        migrationBuilder.DropCheckConstraint("ck_golden_globe_nominations_enrichment_status", "golden_globe_nominations");
        migrationBuilder.DropColumn("last_enrichment_error", "golden_globe_nominations");
        migrationBuilder.DropColumn("next_enrichment_attempt_at", "golden_globe_nominations");
        migrationBuilder.DropColumn("last_enrichment_attempt_at", "golden_globe_nominations");
        migrationBuilder.DropColumn("enrichment_attempt_count", "golden_globe_nominations");
        migrationBuilder.DropColumn("enrichment_status", "golden_globe_nominations");
    }
}
