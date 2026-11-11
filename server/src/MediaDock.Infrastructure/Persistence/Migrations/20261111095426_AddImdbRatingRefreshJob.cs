using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImdbRatingRefreshJob : Migration
    {
        /// <inheritdoc />
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

            migrationBuilder.CreateIndex(
                name: "ux_background_jobs_active_imdb_rating_refresh",
                table: "background_jobs",
                column: "job_type",
                unique: true,
                filter: "job_type = 'imdb_rating_refresh' AND status IN ('queued', 'running')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_input_type",
                table: "background_jobs",
                sql: "(job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment', 'imdb_catalog_enrichment', 'imdb_rating_refresh', 'omdb_quota_recovery') AND input_bytes IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type",
                table: "background_jobs",
                sql: "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'imdb_title_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment', 'imdb_catalog_enrichment', 'imdb_rating_refresh', 'omdb_quota_recovery')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger IN ('manual', 'schedule')) OR (job_type = 'omdb_quota_recovery' AND trigger = 'schedule') OR (job_type IN ('oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger = 'quota_recovery') OR (job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import', 'imdb_rating_refresh', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND trigger = 'manual')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_background_jobs_active_imdb_rating_refresh",
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
                sql: "(job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment', 'imdb_catalog_enrichment', 'omdb_quota_recovery') AND input_bytes IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type",
                table: "background_jobs",
                sql: "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'imdb_title_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment', 'imdb_catalog_enrichment', 'omdb_quota_recovery')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger IN ('manual', 'schedule')) OR (job_type = 'omdb_quota_recovery' AND trigger = 'schedule') OR (job_type IN ('oscar_enrichment', 'golden_globe_enrichment', 'imdb_catalog_enrichment') AND trigger = 'quota_recovery') OR (job_type IN ('oscar_import', 'golden_globe_import', 'imdb_title_import', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND trigger = 'manual')");
        }
    }
}
