using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowQuotaRecoveryEnrichmentTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_trigger",
                table: "background_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_trigger",
                table: "background_jobs",
                sql: "trigger IN ('manual', 'schedule', 'quota_recovery')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger IN ('manual', 'schedule')) OR (job_type = 'omdb_quota_recovery' AND trigger = 'schedule') OR (job_type IN ('oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger = 'quota_recovery') OR (job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND trigger = 'manual')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_trigger",
                table: "background_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_trigger",
                table: "background_jobs",
                sql: "trigger IN ('manual', 'schedule')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger IN ('manual', 'schedule')) OR (job_type = 'omdb_quota_recovery' AND trigger = 'schedule') OR (job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND trigger = 'manual')");
        }
    }
}
