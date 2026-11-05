using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleAwardEnrichmentJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND trigger = 'manual')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type IN ('rss_scan', 'imdb_catalog_enrichment') AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND trigger = 'manual')");
        }
    }
}
