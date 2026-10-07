using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoldenGlobeManualImdbLinks : Migration
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

            migrationBuilder.AddColumn<int>(
                name: "imdb_id_version",
                table: "golden_globe_nominations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_imdb_id_manual",
                table: "golden_globe_nominations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_input_type",
                table: "background_jobs",
                sql: "(job_type IN ('oscar_import', 'golden_globe_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh') AND input_bytes IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type",
                table: "background_jobs",
                sql: "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh') AND trigger = 'manual')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropColumn(
                name: "imdb_id_version",
                table: "golden_globe_nominations");

            migrationBuilder.DropColumn(
                name: "is_imdb_id_manual",
                table: "golden_globe_nominations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_input_type",
                table: "background_jobs",
                sql: "(job_type IN ('oscar_import', 'golden_globe_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment') AND input_bytes IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type",
                table: "background_jobs",
                sql: "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment') AND trigger = 'manual')");
        }
    }
}
