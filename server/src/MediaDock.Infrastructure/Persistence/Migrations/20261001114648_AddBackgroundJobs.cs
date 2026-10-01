using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MediaDock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackgroundJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "background_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_slot_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scan_run_id = table.Column<long>(type: "bigint", nullable: true),
                    current_stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    current_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    progress_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    result_summary = table.Column<string>(type: "jsonb", nullable: true),
                    input_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    input_content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    input_bytes = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_jobs", x => x.id);
                    table.CheckConstraint("ck_background_jobs_input_type", "(job_type = 'oscar_import' AND input_bytes IS NOT NULL) OR (job_type = 'rss_scan' AND input_bytes IS NULL)");
                    table.CheckConstraint("ck_background_jobs_lifecycle", "(status = 'queued' AND started_at IS NULL AND finished_at IS NULL) OR (status = 'running' AND started_at IS NOT NULL AND finished_at IS NULL) OR (status IN ('succeeded', 'partial', 'failed') AND started_at IS NOT NULL AND finished_at IS NOT NULL)");
                    table.CheckConstraint("ck_background_jobs_status", "status IN ('queued', 'running', 'succeeded', 'partial', 'failed')");
                    table.CheckConstraint("ck_background_jobs_trigger", "trigger IN ('manual', 'schedule')");
                    table.CheckConstraint("ck_background_jobs_type", "job_type IN ('rss_scan', 'oscar_import')");
                    table.CheckConstraint("ck_background_jobs_type_trigger", "(job_type = 'rss_scan' AND trigger IN ('manual', 'schedule')) OR (job_type = 'oscar_import' AND trigger = 'manual')");
                    table.ForeignKey(
                        name: "fk_background_jobs_scan_runs_scan_run_id",
                        column: x => x.scan_run_id,
                        principalTable: "scan_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "background_scheduler_state",
                columns: table => new
                {
                    schedule_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_evaluated_slot_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_scheduler_state", x => x.schedule_name);
                });

            migrationBuilder.CreateTable(
                name: "background_job_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    event_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_background_job_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_background_job_events_background_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "background_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_background_job_events_job_id_id",
                table: "background_job_events",
                columns: new[] { "job_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_background_jobs_status_enqueued_at",
                table: "background_jobs",
                columns: new[] { "status", "enqueued_at" });

            migrationBuilder.CreateIndex(
                name: "ux_background_jobs_active_rss_scan",
                table: "background_jobs",
                column: "job_type",
                unique: true,
                filter: "job_type = 'rss_scan' AND status IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "ux_background_jobs_scan_run_id",
                table: "background_jobs",
                column: "scan_run_id",
                unique: true,
                filter: "scan_run_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_background_jobs_scheduled_slot_utc",
                table: "background_jobs",
                column: "scheduled_slot_utc",
                unique: true,
                filter: "scheduled_slot_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "background_job_events");

            migrationBuilder.DropTable(
                name: "background_scheduler_state");

            migrationBuilder.DropTable(
                name: "background_jobs");
        }
    }
}
