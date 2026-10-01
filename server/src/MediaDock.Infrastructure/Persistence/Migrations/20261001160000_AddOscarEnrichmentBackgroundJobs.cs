using MediaDock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MediaDockDbContext))]
[Migration("20261001160000_AddOscarEnrichmentBackgroundJobs")]
public partial class AddOscarEnrichmentBackgroundJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_background_jobs_input_type",
            table: "background_jobs");
        migrationBuilder.DropCheckConstraint(
            name: "ck_background_jobs_type",
            table: "background_jobs");
        migrationBuilder.DropCheckConstraint(
            name: "ck_background_jobs_type_trigger",
            table: "background_jobs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_background_jobs_input_type",
            table: "background_jobs",
            sql: "(job_type = 'oscar_import' AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment') AND input_bytes IS NULL)");
        migrationBuilder.AddCheckConstraint(
            name: "ck_background_jobs_type",
            table: "background_jobs",
            sql: "job_type IN ('rss_scan', 'oscar_import', 'oscar_enrichment')");
        migrationBuilder.AddCheckConstraint(
            name: "ck_background_jobs_type_trigger",
            table: "background_jobs",
            sql: "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'oscar_enrichment') AND trigger = 'manual')");
        migrationBuilder.CreateIndex(
            name: "ux_background_jobs_active_oscar_enrichment",
            table: "background_jobs",
            column: "job_type",
            unique: true,
            filter: "job_type = 'oscar_enrichment' AND status IN ('queued', 'running')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_background_jobs_active_oscar_enrichment",
            table: "background_jobs");
        migrationBuilder.DropCheckConstraint(
            name: "ck_background_jobs_input_type",
            table: "background_jobs");
        migrationBuilder.DropCheckConstraint(
            name: "ck_background_jobs_type",
            table: "background_jobs");
        migrationBuilder.DropCheckConstraint(
            name: "ck_background_jobs_type_trigger",
            table: "background_jobs");

        migrationBuilder.AddCheckConstraint(
            name: "ck_background_jobs_input_type",
            table: "background_jobs",
            sql: "(job_type = 'oscar_import' AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type = 'rss_scan' AND input_bytes IS NULL)");
        migrationBuilder.AddCheckConstraint(
            name: "ck_background_jobs_type",
            table: "background_jobs",
            sql: "job_type IN ('rss_scan', 'oscar_import')");
        migrationBuilder.AddCheckConstraint(
            name: "ck_background_jobs_type_trigger",
            table: "background_jobs",
            sql: "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type = 'oscar_import' AND trigger = 'manual')");
    }
}