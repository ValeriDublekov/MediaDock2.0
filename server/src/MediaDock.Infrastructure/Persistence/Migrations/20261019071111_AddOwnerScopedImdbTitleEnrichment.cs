using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerScopedImdbTitleEnrichment : Migration
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

            migrationBuilder.AddColumn<long>(
                name: "owner_user_id",
                table: "background_jobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_background_jobs_active_imdb_title_enrichment_owner",
                table: "background_jobs",
                column: "owner_user_id",
                unique: true,
                filter: "job_type = 'imdb_title_enrichment' AND status IN ('queued', 'running')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_input_type",
                table: "background_jobs",
                sql: "(job_type IN ('oscar_import', 'golden_globe_import') AND (input_bytes IS NOT NULL OR status IN ('succeeded', 'partial', 'failed'))) OR (job_type IN ('rss_scan', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND input_bytes IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_owner_user_id",
                table: "background_jobs",
                sql: "(job_type = 'imdb_title_enrichment' AND owner_user_id IS NOT NULL) OR (job_type <> 'imdb_title_enrichment' AND owner_user_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type",
                table: "background_jobs",
                sql: "job_type IN ('rss_scan', 'oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs",
                sql: "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type IN ('oscar_import', 'golden_globe_import', 'oscar_enrichment', 'golden_globe_enrichment', 'golden_globe_manual_refresh', 'imdb_title_enrichment') AND trigger = 'manual')");

            migrationBuilder.AddForeignKey(
                name: "fk_background_jobs_users_owner_user_id",
                table: "background_jobs",
                column: "owner_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_background_jobs_users_owner_user_id",
                table: "background_jobs");

            migrationBuilder.DropIndex(
                name: "ux_background_jobs_active_imdb_title_enrichment_owner",
                table: "background_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_input_type",
                table: "background_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_owner_user_id",
                table: "background_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_type",
                table: "background_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_background_jobs_type_trigger",
                table: "background_jobs");

            migrationBuilder.DropColumn(
                name: "owner_user_id",
                table: "background_jobs");

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
    }
}
